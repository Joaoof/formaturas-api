using FormaturasFlow.Api.Domain;

namespace FormaturasFlow.Api.Payments;

/*  Domínio não habilita o método pedido (ex.: Pix em Casamento).  */
public sealed class MetodoPagamentoNaoSuportadoException : DomainException
{
    public const string CodigoErro = "PAGAMENTO_METODO_NAO_SUPORTADO";

    public MetodoPagamentoNaoSuportadoException(
        TipoProjeto projeto,
        MetodoPagamento metodo,
        IReadOnlyList<MetodoPagamento> suportados)
        : base(CodigoErro,
            $"O domínio {projeto} não aceita pagamento via {metodo}. " +
            $"Métodos habilitados: {string.Join(", ", suportados)}.")
    {
        Projeto   = projeto;
        Metodo    = metodo;
        Suportados = suportados;

        Detalhes["tipoProjeto"]      = projeto.ToString();
        Detalhes["metodo"]           = metodo.ToString();
        Detalhes["metodosSuportados"] = suportados.Select(m => m.ToString()).ToArray();
    }

    public TipoProjeto                    Projeto    { get; }
    public MetodoPagamento                Metodo     { get; }
    public IReadOnlyList<MetodoPagamento> Suportados { get; }
}

/*  Tentativa de mandar a cobrança de um domínio para o PSP do outro
    (ex.: Casamento → Cora).  É a blindagem do isolamento financeiro.  */
public sealed class ProviderNaoPermitidoException : DomainException
{
    public const string CodigoErro = "PAGAMENTO_PROVIDER_NAO_PERMITIDO";

    public ProviderNaoPermitidoException(
        TipoProjeto projeto,
        PaymentProvider solicitado,
        PaymentProvider permitido)
        : base(CodigoErro,
            $"O domínio {projeto} opera exclusivamente com {permitido}; " +
            $"roteamento para {solicitado} é proibido.")
    {
        Projeto    = projeto;
        Solicitado = solicitado;
        Permitido  = permitido;

        Detalhes["tipoProjeto"]        = projeto.ToString();
        Detalhes["providerSolicitado"] = solicitado.ToString();
        Detalhes["providerPermitido"]  = permitido.ToString();
    }

    public TipoProjeto     Projeto    { get; }
    public PaymentProvider Solicitado { get; }
    public PaymentProvider Permitido  { get; }
}

/*  O payload chegou incompleto para o método escolhido (ex.: cartão sem
    CEP do portador, exigido pelo Asaas).  Separado do 400 genérico para
    que o front consiga apontar o campo exato.  */
public sealed class DadosPagamentoIncompletosException : DomainException
{
    public const string CodigoErro = "PAGAMENTO_DADOS_INCOMPLETOS";

    public DadosPagamentoIncompletosException(string campo, string mensagem)
        : base(CodigoErro, mensagem)
    {
        Campo = campo;

        Detalhes["campo"] = campo;
    }

    public string Campo { get; }
}
