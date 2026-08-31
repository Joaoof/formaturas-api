namespace FormaturasFlow.Api.Payments;

/*  Adapter: cada PSP implementa esta interface traduzindo o contrato
    interno (CobrancaRequest/CobrancaCriada) para o payload próprio.

    `Suporta` expressa CAPACIDADE TÉCNICA do PSP — não permissão de
    negócio.  Quem decide o que é permitido por domínio é o
    IPaymentRouter, a partir da PaymentRoutingPolicy.  */
public interface IPaymentGateway
{
    PaymentProvider Provider { get; }

    bool Suporta(MetodoPagamento metodo);

    Task<CobrancaCriada> CriarCobrancaAsync(CobrancaRequest req, CancellationToken ct = default);
}
