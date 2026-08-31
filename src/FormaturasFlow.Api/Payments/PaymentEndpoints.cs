using FormaturasFlow.Api.Data;

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
        CartaoCreditoInfo? Cartao = null);

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
                """)
            .Produces<CobrancaResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
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
        CancellationToken ct)
    {
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
