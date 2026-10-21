namespace FormaturasFlow.Api.Payments;

/*  A matriz de isolamento financeiro como DADO, não como `if` espalhado.

    Manter isso como tabela (e não como switch dentro da factory) é o
    que permite liberar Pix para Casamento amanhã — quando o negócio
    aprovar — mexendo em uma linha do composition root, sem recompilar
    regra nenhuma de use case.  */
public sealed record PaymentRoutingPolicy(
    PaymentProvider Provider,
    IReadOnlySet<MetodoPagamento> Metodos,
    IReadOnlyDictionary<MetodoPagamento, PaymentProvider>? ProviderPorMetodo = null,
    IReadOnlyDictionary<MetodoPagamento, IReadOnlySet<PaymentProvider>>? ProvedoresAlternativos = null)
{
    private static readonly IReadOnlySet<PaymentProvider> Nenhum = new HashSet<PaymentProvider>();

    /*  Provider efetivo de um método: usa o override quando existir, senão o
        provider primário do domínio.  É o que permite a Formatura ficar na
        Cora (boleto/Pix) e ainda assim mandar o CARTÃO para o Asaas, que é
        quem processa cartão.  */
    public PaymentProvider ProviderDoMetodo(MetodoPagamento metodo) =>
        ProviderPorMetodo is not null && ProviderPorMetodo.TryGetValue(metodo, out var p) ? p : Provider;

    /*  Provedores que o método TAMBÉM aceita além do padrão — escolhidos por
        turma.  Ex.: o boleto/Pix da Formatura pode ir pra Cora (padrão) OU
        Asaas, conforme o banco que a empresa definiu naquela turma.  */
    public IReadOnlySet<PaymentProvider> AlternativasDoMetodo(MetodoPagamento metodo) =>
        ProvedoresAlternativos is not null && ProvedoresAlternativos.TryGetValue(metodo, out var s) ? s : Nenhum;

    /*  Casamentos → Asaas (cartão + boleto).  Formaturas → Cora (boleto +
        Pix) por padrão, cartão via Asaas, e boleto/Pix podem ir pro Asaas
        quando a turma escolhe esse banco.  */
    public static readonly IReadOnlyDictionary<TipoProjeto, PaymentRoutingPolicy> Padrao =
        new Dictionary<TipoProjeto, PaymentRoutingPolicy>
        {
            [TipoProjeto.Casamento] = new(PaymentProvider.Asaas,
                new HashSet<MetodoPagamento> { MetodoPagamento.CartaoCredito, MetodoPagamento.Boleto }),

            [TipoProjeto.Formatura] = new(PaymentProvider.Cora,
                new HashSet<MetodoPagamento> { MetodoPagamento.Boleto, MetodoPagamento.Pix, MetodoPagamento.CartaoCredito },
                new Dictionary<MetodoPagamento, PaymentProvider>
                {
                    [MetodoPagamento.CartaoCredito] = PaymentProvider.Asaas
                },
                new Dictionary<MetodoPagamento, IReadOnlySet<PaymentProvider>>
                {
                    [MetodoPagamento.Boleto] = new HashSet<PaymentProvider> { PaymentProvider.Asaas },
                    [MetodoPagamento.Pix]    = new HashSet<PaymentProvider> { PaymentProvider.Asaas }
                })
        };
}
