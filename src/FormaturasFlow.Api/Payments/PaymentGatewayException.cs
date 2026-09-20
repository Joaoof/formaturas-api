namespace FormaturasFlow.Api.Payments;

/*  Falha de INFRAESTRUTURA do PSP (rede, credencial, 5xx).  Não é
    DomainException de propósito: o usuário não fez nada errado, então o
    front deve oferecer "tentar de novo", não corrigir o formulário.  */
public class PaymentGatewayException(PaymentProvider provider, string mensagem) : Exception(mensagem)
{
    public PaymentProvider Provider { get; } = provider;
}
