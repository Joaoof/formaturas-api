namespace FormaturasFlow.Api.Payments;

/*  Factory + Strategy: recebe a matriz e todos os adapters registrados,
    e é o único componente autorizado a casar domínio com PSP.

    Erro de NEGÓCIO (cruzamento indevido) vira DomainException → 422 com
    código estável para o front.  Erro de CONFIGURAÇÃO (política sem PSP
    correspondente no contêiner) vira InvalidOperationException → 500,
    porque é bug nosso, não escolha do usuário.  */
public sealed class PaymentGatewayFactory : IPaymentRouter
{
    private readonly IReadOnlyDictionary<TipoProjeto, PaymentRoutingPolicy> _politicas;
    private readonly IReadOnlyDictionary<PaymentProvider, IPaymentGateway>  _gateways;

    public PaymentGatewayFactory(
        IReadOnlyDictionary<TipoProjeto, PaymentRoutingPolicy> politicas,
        IEnumerable<IPaymentGateway> gateways)
    {
        _politicas = politicas;

        _gateways = gateways
            .GroupBy(g => g.Provider)
            .ToDictionary(g => g.Key, g => g.Count() == 1
                ? g.First()
                : throw new InvalidOperationException($"Mais de um IPaymentGateway registrado para {g.Key}."));
    }

    public PaymentProvider ProviderDe(TipoProjeto projeto) =>
        PoliticaDe(projeto).Provider;

    public IReadOnlyList<MetodoPagamento> MetodosSuportados(TipoProjeto projeto) =>
        PoliticaDe(projeto).Metodos.Order().ToArray();

    public IPaymentGateway Resolver(TipoProjeto projeto, MetodoPagamento metodo)
    {
        var politica = PoliticaDe(projeto);

        if (!politica.Metodos.Contains(metodo))
            throw new MetodoPagamentoNaoSuportadoException(projeto, metodo, MetodosSuportados(projeto));

        if (!_gateways.TryGetValue(politica.Provider, out var gateway))
            throw new InvalidOperationException(
                $"Política de {projeto} exige {politica.Provider}, mas nenhum IPaymentGateway desse provider foi registrado.");

        /*  Defesa em profundidade: a política pode ter liberado um método
            que o PSP não implementa.  Falhar aqui é melhor do que enviar
            payload inválido e receber um 400 opaco do provedor.  */
        if (!gateway.Suporta(metodo))
            throw new MetodoPagamentoNaoSuportadoException(
                projeto, metodo, MetodosSuportados(projeto).Where(gateway.Suporta).ToArray());

        return gateway;
    }

    public IPaymentGateway Resolver(TipoProjeto projeto, MetodoPagamento metodo, PaymentProvider providerExigido)
    {
        var politica = PoliticaDe(projeto);

        if (politica.Provider != providerExigido)
            throw new ProviderNaoPermitidoException(projeto, providerExigido, politica.Provider);

        return Resolver(projeto, metodo);
    }

    private PaymentRoutingPolicy PoliticaDe(TipoProjeto projeto) =>
        _politicas.TryGetValue(projeto, out var politica)
            ? politica
            : throw new InvalidOperationException($"Nenhuma política de roteamento configurada para {projeto}.");
}
