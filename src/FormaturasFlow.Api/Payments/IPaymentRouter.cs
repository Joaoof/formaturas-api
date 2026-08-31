namespace FormaturasFlow.Api.Payments;

/*  Ponto único de acoplamento entre domínio de negócio e PSP.

    Endpoints e use cases dependem só desta interface: eles nunca citam
    Asaas nem Cora, então trocar de PSP (ou liberar um método novo) é
    mudança de política + composition root, sem tocar em caso de uso.  */
public interface IPaymentRouter
{
    /*  PSP obrigatório do domínio.  */
    PaymentProvider ProviderDe(TipoProjeto projeto);

    /*  Métodos habilitados — é o que o front consome para montar o
        checkout só com o que existe.  */
    IReadOnlyList<MetodoPagamento> MetodosSuportados(TipoProjeto projeto);

    /*  Resolve o gateway.  Lança DomainException se o método não for
        habilitado para o domínio.  */
    IPaymentGateway Resolver(TipoProjeto projeto, MetodoPagamento metodo);

    /*  Sobrecarga defensiva para chamadores que já carregam um provider
        (reprocessamento, conciliação, webhook).  Lança DomainException
        se o provider exigido não for o do domínio.  */
    IPaymentGateway Resolver(TipoProjeto projeto, MetodoPagamento metodo, PaymentProvider providerExigido);
}
