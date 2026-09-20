using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Payments;

public static class PaymentEndpoints
{
    public record MetodosResponse(string TipoProjeto, string Provider, IReadOnlyList<string> Metodos);

    /*  Enums viajam como string no corpo: o front manda "Casamento", não 0.
        Feito por parse manual em vez de JsonStringEnumConverter global para
        não mudar a serialização dos endpoints já existentes.  */
    public record CobrancaRoteadaRequest(
        string             TipoProjeto,
        string             Metodo,
        decimal            Valor,
        DateOnly           Vencimento,
        string             Descricao,
        string             ReferenciaExterna,
        PagadorInfo        Pagador,
        CartaoCreditoInfo? Cartao = null,

        /*  Opcional, mas é o que fecha o ciclo: sem a parcela, a cobrança
            nasce órfã e o webhook do PSP não tem o que baixar depois.  */
        Guid?              ParcelaId = null);

    public record CobrancaResponse(
        string  Provider,
        string  ChargeId,
        string  Metodo,
        string  Status,
        string? LinkPagamento,
        string? BoletoUrl,
        string? BoletoLinhaDigitavel,
        string? BoletoCodigoBarras,
        string? PixCopiaCola,
        string? PixQrCodeUrl);

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pagamentos/metodos/{tipoProjeto}", MetodosDisponiveis)
            .RequireAuthorization()
            .WithTags("Pagamentos")
            .WithSummary("Métodos de pagamento habilitados para o domínio")
            .WithDescription("""
                Fonte da verdade do checkout: o front monta a tela a partir desta
                lista, em vez de hard-codear "cartão, boleto e Pix" e descobrir no
                POST que o domínio não aceita.
                Casamento → Asaas (CartaoCredito, Boleto). Formatura → Cora (Boleto, Pix).
                """)
            .Produces<MetodosResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/pagamentos/cobrancas", CriarCobrancaAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithTags("Pagamentos")
            .WithSummary("Emite cobrança pelo PSP do domínio")
            .WithDescription("""
                O PSP não é escolhido pelo chamador: o IPaymentRouter deriva do
                `tipoProjeto`. Casamento → Asaas, Formatura → Cora.
                Cruzamento indevido (ex.: Casamento + Pix) retorna 422 com
                `codigo` e `metodosSuportados`. Falha do provedor retorna 502.
                Informe `parcelaId` para vincular a cobrança à parcela: é o
                que permite ao webhook do PSP dar baixa depois.
                """)
            .Produces<CobrancaResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway);

        return app;
    }

    private static IResult MetodosDisponiveis(string tipoProjeto, IPaymentRouter router)
    {
        if (!Enum.TryParse<TipoProjeto>(tipoProjeto, ignoreCase: true, out var projeto))
            return Results.BadRequest(new
            {
                codigo = "TIPO_PROJETO_INVALIDO",
                erro = $"Tipo de projeto inválido. Use: {string.Join(", ", Enum.GetNames<TipoProjeto>())}."
            });

        return Results.Ok(new MetodosResponse(
            TipoProjeto: projeto.ToString(),
            Provider: router.ProviderDe(projeto).ToString(),
            Metodos: router.MetodosSuportados(projeto).Select(m => m.ToString()).ToArray()));
    }

    private static async Task<IResult> CriarCobrancaAsync(
        CobrancaRoteadaRequest req,
        IPaymentRouter router,
        AppDbContext db,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var log = logs.CreateLogger("Pagamentos.Cobrancas");

        if (!Enum.TryParse<TipoProjeto>(req.TipoProjeto, ignoreCase: true, out var projeto))
            return Results.BadRequest(new
            {
                codigo = "TIPO_PROJETO_INVALIDO",
                erro = $"Tipo de projeto inválido. Use: {string.Join(", ", Enum.GetNames<TipoProjeto>())}."
            });

        if (!Enum.TryParse<MetodoPagamento>(req.Metodo, ignoreCase: true, out var metodo))
            return Results.BadRequest(new
            {
                codigo = "METODO_PAGAMENTO_INVALIDO",
                erro = $"Método inválido. Use: {string.Join(", ", Enum.GetNames<MetodoPagamento>())}."
            });

        /*  Parcela é carregada ANTES de emitir: descobrir que o id não
            existe depois da cobrança criada deixaria dinheiro pendurado no
            PSP sem dono.  */
        Parcela? parcela = null;
        if (req.ParcelaId is { } parcelaId)
        {
            parcela = await db.Parcelas.FirstOrDefaultAsync(p => p.Id == parcelaId, ct);
            if (parcela is null)
                return Results.BadRequest(new
                {
                    codigo = "PARCELA_NAO_ENCONTRADA",
                    erro = $"Parcela {parcelaId} não existe."
                });

            if (parcela.Status == StatusParcela.Pago)
                return Results.Conflict(new { codigo = "PARCELA_JA_QUITADA", erro = "Parcela já quitada." });
        }

        /*  O caso de uso não conhece Asaas nem Cora: pede o gateway ao
            router e a matriz decide (ou lança DomainException → 422).  */
        var gateway = router.Resolver(projeto, metodo);

        var cobranca = await gateway.CriarCobrancaAsync(new CobrancaRequest(
            Metodo: metodo,
            Valor: req.Valor,
            Vencimento: req.Vencimento,
            Descricao: req.Descricao,
            ReferenciaExterna: req.ReferenciaExterna,
            Pagador: req.Pagador,
            Cartao: req.Cartao), ct);

        if (parcela is not null)
        {
            parcela.PspProvider = cobranca.Provider.ToString().ToLowerInvariant();
            parcela.PspChargeId = cobranca.ChargeId;
            parcela.PspStatus = cobranca.Status;
            parcela.FormaPagamento = metodo.ToString();
            parcela.BoletoUrl = cobranca.BoletoUrl;
            parcela.BoletoLinhaDigitavel = cobranca.BoletoLinhaDigitavel;
            parcela.BoletoCodigoBarras = cobranca.BoletoCodigoBarras;
            parcela.PixCopiaCola = cobranca.PixCopiaCola;
            parcela.PixQrCodeUrl = cobranca.PixQrCodeUrl;
            parcela.LinkPagamento = cobranca.LinkPagamento;
            parcela.AtualizadaEm = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                /*  A cobrança JÁ existe no PSP e o vínculo não persistiu: se
                    ficar assim, o pagamento entra e o webhook não acha a
                    parcela para baixar.

                    Falhar alto é o certo aqui porque a recuperação é segura:
                    a chave de idempotência é derivada da `referenciaExterna`,
                    então repetir a mesma emissão devolve a MESMA fatura em
                    vez de cobrar o formando de novo.  O log carrega tudo que
                    a reconciliação manual precisa.  */
                log.LogCritical(ex,
                    "Cobrança {ChargeId} criada em {Provider} mas o vínculo com a parcela {ParcelaId} não foi salvo. "
                    + "Reemitir com referenciaExterna={Referencia} devolve a mesma fatura.",
                    cobranca.ChargeId, cobranca.Provider, parcela.Id, req.ReferenciaExterna);

                throw;
            }
        }

        return Results.Ok(new CobrancaResponse(
            Provider: cobranca.Provider.ToString(),
            ChargeId: cobranca.ChargeId,
            Metodo: cobranca.Metodo.ToString(),
            Status: cobranca.Status,
            LinkPagamento: cobranca.LinkPagamento,
            BoletoUrl: cobranca.BoletoUrl,
            BoletoLinhaDigitavel: cobranca.BoletoLinhaDigitavel,
            BoletoCodigoBarras: cobranca.BoletoCodigoBarras,
            PixCopiaCola: cobranca.PixCopiaCola,
            PixQrCodeUrl: cobranca.PixQrCodeUrl));
    }
}
