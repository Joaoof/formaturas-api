using FormaturasFlow.Api.Data;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

/*  Administração do webhook da Cora.

    Existe porque a Integração Direta não tem tela para isso: o endereço que
    recebe o aviso de pagamento só entra por API.  Sem esse cadastro o boleto
    é pago e o sistema nunca sabe — a parcela fica pendente com o dinheiro já
    na conta.  Deixar como curl na mão de alguém é receita para esquecer no
    dia da virada.  */
public static class CoraAdminEndpoints
{
    public record EndpointResponse(
        string Id,
        string Resource,
        string Trigger,
        bool   Active,
        bool   IncluiRecurso,

        /*  A URL cadastrada carrega o segredo na query; devolver inteira
            vazaria esse segredo para qualquer funcionário com acesso ao
            painel.  Só o host e o caminho saem daqui.  */
        string UrlSemSegredo);

    public record CadastrarRequest(
        string? Url = null,
        string  Recurso = "invoice",
        string  Gatilho = "*",
        bool    IncluirRecurso = false);

    public static IEndpointRouteBuilder MapCoraAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/pagamentos/cora/endpoints")
            .WithTags("Pagamentos")
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin));

        grupo.MapGet("/", ListarAsync)
            .WithSummary("Lista os endpoints de notificação cadastrados na Cora")
            .WithDescription("""
                Mostra para onde a Cora está entregando os avisos de pagamento.
                Lista vazia significa que NINGUÉM será avisado quando um
                boleto ou Pix for pago.
                A URL vem sem a query, porque ela carrega o segredo do webhook.
                """)
            .Produces<EndpointResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status502BadGateway);

        grupo.MapPost("/", CadastrarAsync)
            .WithSummary("Cadastra o endpoint de notificação na Cora")
            .WithDescription("""
                Sem `url`, monta sozinho a partir do endereço público desta API
                (`Cora:WebhookUrlPublica`) e anexa o `Cora:WebhookSecret` na
                query — que é o único jeito, porque a Cora não aceita cabeçalho
                customizado no aviso.

                Por padrão assina todos os gatilhos de fatura (`invoice.*`):
                marcar demais é seguro, porque o sistema reconsulta a Cora
                antes de dar qualquer parcela como paga, e marcar de menos faz
                perder confirmação de pagamento.
                """)
            .Produces<EndpointResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway);

        grupo.MapDelete("/{id}", RemoverAsync)
            .WithSummary("Remove um endpoint de notificação da Cora")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status502BadGateway);

        return app;
    }

    private static async Task<IResult> ListarAsync(CoraEndpointsClient cora, CancellationToken ct)
    {
        var lista = await cora.ListarAsync(ct);
        return Results.Ok(lista.Select(Montar).ToArray());
    }

    private static async Task<IResult> CadastrarAsync(
        CadastrarRequest req,
        CoraEndpointsClient cora,
        IOptions<CoraOptions> opt,
        CancellationToken ct)
    {
        var url = req.Url;

        if (string.IsNullOrWhiteSpace(url))
        {
            var resultado = UrlDoWebhook(opt.Value);
            if (resultado.Erro is not null)
                return Results.BadRequest(new { codigo = resultado.Codigo, erro = resultado.Erro });

            url = resultado.Url;
        }

        var criado = await cora.CriarAsync(url!, req.Recurso, req.Gatilho, req.IncluirRecurso, ct);

        return Results.Created($"/api/v1/pagamentos/cora/endpoints/{criado.Id}", Montar(criado));
    }

    private static async Task<IResult> RemoverAsync(string id, CoraEndpointsClient cora, CancellationToken ct)
    {
        await cora.RemoverAsync(id, ct);
        return Results.NoContent();
    }

    /*  Monta a URL do aviso com o segredo embutido.  Separado e interno para
        poder ser testado sem subir a aplicação inteira.  */
    internal static (string? Url, string? Codigo, string? Erro) UrlDoWebhook(CoraOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.WebhookUrlPublica))
            return (null, "WEBHOOK_URL_NAO_CONFIGURADA",
                "Defina Cora:WebhookUrlPublica com o endereço público desta API, ou informe `url` no corpo.");

        if (string.IsNullOrWhiteSpace(o.WebhookSecret))
            return (null, "WEBHOOK_SECRET_NAO_CONFIGURADO",
                "Defina Cora:WebhookSecret antes de cadastrar o endpoint: sem ele o webhook recusa todo aviso com 503.");

        var baseUrl = o.WebhookUrlPublica.TrimEnd('/');
        var caminho = $"{baseUrl}/api/v1/pagamentos/webhooks/cora";

        return ($"{caminho}?secret={Uri.EscapeDataString(o.WebhookSecret)}", null, null);
    }

    private static EndpointResponse Montar(CoraEndpointsClient.Endpoint e) => new(
        Id: e.Id,
        Resource: e.Resource,
        Trigger: e.Trigger,
        Active: e.Active,
        IncluiRecurso: e.IncludeResource,
        UrlSemSegredo: SemQuery(e.Url));

    internal static string SemQuery(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
            ? $"{u.Scheme}://{u.Host}{u.AbsolutePath}"
            : url;
}
