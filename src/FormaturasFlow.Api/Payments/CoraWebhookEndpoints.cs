using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

/*  Recebimento de notificação da Cora + consulta de status.

    Decisão central: o corpo do webhook é tratado como AVISO, nunca como
    verdade.  Dele extraímos somente o id da fatura e, em seguida,
    reconsultamos a Cora por mTLS — canal que um terceiro não consegue
    forjar.  Isso vale o custo de uma chamada extra porque elimina de uma vez
    duas classes de problema: dar parcela como paga a partir de um POST
    forjado, e quebrar a cada ajuste de payload do provedor.  */
public static class CoraWebhookEndpoints
{
    public const string RateLimitPolicy = "webhook-cora";

    public record StatusCobrancaResponse(
        string   Provider,
        string   ChargeId,
        string   Metodo,
        string   Status,
        bool     Pago,
        bool     Parcial,
        decimal? ValorPago,
        string?  LinkPagamento,
        string?  BoletoUrl,
        string?  BoletoLinhaDigitavel,
        string?  BoletoCodigoBarras,
        string?  PixCopiaCola,
        string?  PixQrCodeUrl);

    /*  QUITADO, não apenas "entrou dinheiro".  `PAID_PARTIALLY` fica de fora
        de propósito: tratá-lo como quitado daria baixa numa parcela de
        R$ 1.000 que recebeu R$ 300, e o saldo nunca seria cobrado.  */
    private static readonly string[] StatusQuitados = ["PAID", "SETTLED"];

    /*  Houve recebimento, integral ou não — serve para atualizar o valor
        pago sem necessariamente quitar.  */
    private static readonly string[] StatusComRecebimento = ["PAID", "SETTLED", "PAID_PARTIALLY"];

    public static IEndpointRouteBuilder MapCoraWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pagamentos/cobrancas/cora/{chargeId}", ConsultarAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithTags("Pagamentos")
            .WithSummary("Consulta a situação de uma cobrança na Cora")
            .WithDescription("""
                Reconsulta a fatura direto na Cora e devolve o contrato já
                normalizado, com `pago` resolvido a partir do status do PSP.
                Serve tanto para o painel quanto para reconciliação manual
                quando o webhook não chegou.
                """)
            .Produces<StatusCobrancaResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status502BadGateway);

        app.MapPost("/pagamentos/webhooks/cora", ReceberAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Pagamentos")
            .WithSummary("Recebe a notificação de pagamento da Cora e baixa a parcela")
            .WithDescription("""
                Anônimo por necessidade: quem chama é a Cora, não um usuário
                logado. Exige `Cora:WebhookSecret` configurado e enviado no
                header `X-Webhook-Secret` ou, se o painel do PSP não permitir
                cabeçalho, na query `?secret=`.
                O corpo serve apenas para descobrir o id da fatura — o estado
                é sempre reconfirmado na API da Cora por mTLS, de modo que um
                POST forjado não marca nada como pago.
                Evento repetido é reconhecido pelo registro em webhook_events
                e não baixa a parcela duas vezes.
                """)
            .Produces<StatusCobrancaResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ConsultarAsync(
        string chargeId,
        IEnumerable<IConsultaCobranca> consultas,
        CancellationToken ct)
    {
        var cora = Cora(consultas);
        if (cora is null)
            return Results.Problem("Consulta da Cora não registrada.", statusCode: StatusCodes.Status500InternalServerError);

        return Results.Ok(Montar(await cora.ConsultarCobrancaAsync(chargeId, ct)));
    }

    private static async Task<IResult> ReceberAsync(
        HttpContext ctx,
        JsonElement corpo,
        AppDbContext db,
        IEnumerable<IConsultaCobranca> consultas,
        IOptions<CoraOptions> opt,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var log = logs.CreateLogger("Pagamentos.Webhook.Cora");
        var segredo = opt.Value.WebhookSecret;

        /*  Sem segredo, o endpoint viraria amplificador: qualquer POST
            anônimo dispararia /token + consulta mTLS na Cora.  503 (e não
            200) porque é falha de configuração nossa — o provedor deve
            reenviar depois que alguém arrumar, em vez de dar o evento por
            entregue e a parcela ficar pendente com dinheiro recebido.  */
        if (string.IsNullOrWhiteSpace(segredo))
        {
            log.LogError("Webhook da Cora recebido sem Cora:WebhookSecret configurado; evento recusado.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!SegredoConfere(ctx, segredo))
        {
            log.LogWarning("Webhook da Cora recusado: segredo ausente ou divergente.");
            return Results.Unauthorized();
        }

        var chargeId = ExtrairChargeId(corpo);
        if (chargeId is null)
        {
            /*  202: recebido e descartado de propósito.  Devolver erro faria
                a Cora reenviar um evento que nunca vamos entender.  */
            log.LogInformation("Webhook da Cora sem id de fatura reconhecível; ignorado.");
            return Results.Accepted();
        }

        var cora = Cora(consultas);
        if (cora is null)
            return Results.Problem("Consulta da Cora não registrada.", statusCode: StatusCodes.Status500InternalServerError);

        var cobranca = await cora.ConsultarCobrancaAsync(chargeId, ct);
        var resposta = Montar(cobranca);

        await RegistrarAsync(db, corpo, resposta, log, ct);

        return Results.Ok(resposta);
    }

    /*  Persistência do evento + baixa da parcela.

        A chave de deduplicação é `chargeId:status`, não o id do evento: a
        Cora pode reenviar a mesma notificação, e o que não pode acontecer é
        a mesma transição ser aplicada duas vezes.  Reenvio depois de uma
        mudança real de status (OPEN → PAID) continua sendo processado.  */
    private static async Task RegistrarAsync(
        AppDbContext db,
        JsonElement corpo,
        StatusCobrancaResponse r,
        ILogger log,
        CancellationToken ct)
    {
        var eventId = $"{r.ChargeId}:{r.Status}";

        if (await db.WebhookEvents.AnyAsync(w => w.Provider == "cora" && w.EventId == eventId, ct))
        {
            log.LogInformation("Webhook da Cora duplicado para {ChargeId} ({Status}); nada a fazer.", r.ChargeId, r.Status);
            return;
        }

        var registro = new WebhookEvent
        {
            Provider = "cora",
            EventId = eventId,
            EventType = r.Status,
            PayloadJson = corpo.GetRawText()
        };
        db.WebhookEvents.Add(registro);

        var parcela = await db.Parcelas.FirstOrDefaultAsync(p => p.PspChargeId == r.ChargeId, ct);

        if (parcela is null)
        {
            /*  Fatura emitida fora do fluxo de parcelas (teste, cobrança
                avulsa).  Guardar o evento ainda importa para auditoria.  */
            log.LogWarning("Webhook da Cora: nenhuma parcela com PspChargeId {ChargeId}.", r.ChargeId);
        }
        else
        {
            parcela.PspStatus = r.Status;
            parcela.AtualizadaEm = DateTimeOffset.UtcNow;

            /*  O valor recebido é atualizado em TODA notificação de
                recebimento, não só na primeira: um parcial seguido da
                quitação precisa terminar com o valor cheio gravado.  */
            if (r.Pago || r.Parcial)
                parcela.ValorPago = r.ValorPago
                    ?? (r.Pago ? parcela.Valor : parcela.ValorPago);

            if (r.Pago && parcela.Status != StatusParcela.Pago)
            {
                parcela.Status = StatusParcela.Pago;
                parcela.DataPagamento = DateOnly.FromDateTime(DateTime.UtcNow);

                log.LogInformation("Parcela {ParcelaId} baixada pelo webhook da Cora ({ChargeId}).",
                    parcela.Id, r.ChargeId);
            }
            else if (r.Parcial)
            {
                /*  Continua PENDENTE: recebimento parcial não quita, e o
                    saldo ainda precisa ser cobrado.  */
                log.LogInformation(
                    "Parcela {ParcelaId} recebeu pagamento PARCIAL de {ValorPago} (devido: {Valor}); segue pendente.",
                    parcela.Id, parcela.ValorPago, parcela.Valor);
            }
        }

        registro.ProcessadoEm = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            /*  Índice único (Provider, EventId): duas entregas simultâneas do
                mesmo evento.  A que perdeu a corrida não tem o que fazer.  */
            log.LogInformation("Webhook da Cora concorrente para {ChargeId}; já registrado.", r.ChargeId);
        }
    }

    private static IConsultaCobranca? Cora(IEnumerable<IConsultaCobranca> consultas) =>
        consultas.FirstOrDefault(c => c.Provider == PaymentProvider.Cora);

    /*  Header é o caminho preferido.  A query existe porque o painel do PSP
        nem sempre deixa configurar cabeçalho — só a URL — e nesse caso a
        alternativa seria o webhook levar 401 para sempre.  É o mesmo arranjo
        já usado no webhook da Efí neste projeto.

        O custo é conhecido: query string costuma aparecer em log de acesso.
        Aceitável aqui porque o segredo apenas barra ruído; quem autoriza a
        baixa é a reconsulta por mTLS, que um segredo vazado não destrava.

        A comparação é em tempo constante, e o valor sai de UM campo
        nomeado: `Contains` num header livre aceitaria qualquer coisa que
        apenas CONTIVESSE o segredo.  */
    private static bool SegredoConfere(HttpContext ctx, string segredo)
    {
        if (ctx.Request.Headers.TryGetValue("X-Webhook-Secret", out var header)
            && Confere(header.ToString(), segredo))
            return true;

        return ctx.Request.Query.TryGetValue("secret", out var query)
            && Confere(query.ToString(), segredo);
    }

    private static bool Confere(string? recebido, string segredo) =>
        !string.IsNullOrEmpty(recebido)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(recebido),
            Encoding.UTF8.GetBytes(segredo));

    /*  A Cora já variou o envelope entre versões (ora a fatura na raiz, ora
        sob `resource`/`data`/`invoice`).  Em vez de casar com um formato
        único, procuramos o id nos lugares plausíveis — é o suficiente,
        porque o dado que importa vem da reconsulta.  */
    internal static string? ExtrairChargeId(JsonElement corpo)
    {
        if (corpo.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var caminho in new[] { "resource", "data", "invoice", "object" })
            if (corpo.TryGetProperty(caminho, out var filho) && filho.ValueKind == JsonValueKind.Object)
                if (IdDe(filho) is { } aninhado)
                    return aninhado;

        return IdDe(corpo);
    }

    private static string? IdDe(JsonElement elemento)
    {
        foreach (var campo in new[] { "id", "invoice_id", "entity_id" })
            if (elemento.TryGetProperty(campo, out var v)
                && v.ValueKind == JsonValueKind.String
                && v.GetString() is { Length: > 0 } texto
                && texto.StartsWith("inv_", StringComparison.Ordinal))
                return texto;

        /*  Fallback: id que não segue o prefixo `inv_` ainda é melhor que
            nada — a reconsulta dirá se existe.  */
        foreach (var campo in new[] { "id", "invoice_id", "entity_id" })
            if (elemento.TryGetProperty(campo, out var v)
                && v.ValueKind == JsonValueKind.String
                && v.GetString() is { Length: > 0 } texto)
                return texto;

        return null;
    }

    internal static StatusCobrancaResponse Montar(CobrancaCriada c) => new(
        Provider: c.Provider.ToString(),
        ChargeId: c.ChargeId,
        Metodo: c.Metodo.ToString(),
        Status: c.Status,
        Pago: StatusQuitados.Contains(c.Status, StringComparer.OrdinalIgnoreCase),
        Parcial: StatusComRecebimento.Contains(c.Status, StringComparer.OrdinalIgnoreCase)
                 && !StatusQuitados.Contains(c.Status, StringComparer.OrdinalIgnoreCase),
        ValorPago: c.ValorPago,
        LinkPagamento: c.LinkPagamento,
        BoletoUrl: c.BoletoUrl,
        BoletoLinhaDigitavel: c.BoletoLinhaDigitavel,
        BoletoCodigoBarras: c.BoletoCodigoBarras,
        PixCopiaCola: c.PixCopiaCola,
        PixQrCodeUrl: c.PixQrCodeUrl);
}
