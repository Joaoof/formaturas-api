namespace FormaturasFlow.Api.Payments;

/*  A matriz de isolamento financeiro como DADO, não como `if` espalhado.

    Manter isso como tabela (e não como switch dentro da factory) é o
    que permite liberar Pix para Casamento amanhã — quando o negócio
    aprovar — mexendo em uma linha do composition root, sem recompilar
    regra nenhuma de use case.  */
public sealed record PaymentRoutingPolicy(PaymentProvider Provider, IReadOnlySet<MetodoPagamento> Metodos)
{
    /*  Casamentos → Asaas (cartão + boleto).  Formaturas → Cora (boleto
        + Pix).  Qualquer cruzamento fora daqui vira DomainException.  */
    public static readonly IReadOnlyDictionary<TipoProjeto, PaymentRoutingPolicy> Padrao =
        new Dictionary<TipoProjeto, PaymentRoutingPolicy>
        {
            [TipoProjeto.Casamento] = new(PaymentProvider.Asaas,
                new HashSet<MetodoPagamento> { MetodoPagamento.CartaoCredito, MetodoPagamento.Boleto }),

            [TipoProjeto.Formatura] = new(PaymentProvider.Cora,
                new HashSet<MetodoPagamento> { MetodoPagamento.Boleto, MetodoPagamento.Pix })
        };
}
