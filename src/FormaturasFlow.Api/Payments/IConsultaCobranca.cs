namespace FormaturasFlow.Api.Payments;

/*  Capacidade opcional do adapter: consultar a cobrança já emitida.

    Fica fora de IPaymentGateway de propósito — nem todo PSP expõe consulta,
    e obrigar quem não expõe a implementar um método que lança seria pior do
    que declarar a capacidade em separado.  */
public interface IConsultaCobranca
{
    PaymentProvider Provider { get; }

    Task<CobrancaCriada> ConsultarCobrancaAsync(string chargeId, CancellationToken ct = default);
}
