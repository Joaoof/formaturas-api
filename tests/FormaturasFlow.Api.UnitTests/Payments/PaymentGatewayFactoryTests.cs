using FormaturasFlow.Api.Payments;
using FluentAssertions;
using Xunit;

namespace FormaturasFlow.Api.UnitTests.Payments;

public class PaymentGatewayFactoryTests
{
    private sealed class GatewayFake(PaymentProvider provider, params MetodoPagamento[] capacidades) : IPaymentGateway
    {
        public PaymentProvider Provider => provider;

        public bool Suporta(MetodoPagamento metodo) => capacidades.Contains(metodo);

        public Task<CobrancaCriada> CriarCobrancaAsync(CobrancaRequest req, CancellationToken ct = default) =>
            Task.FromResult(new CobrancaCriada(provider, "fake-charge", req.Metodo, "PENDENTE"));
    }

    /*  Os fakes declaram a capacidade REAL de cada PSP: o Asaas processa
        Pix, a Cora não processa cartão.  Quem restringe é a política.  */
    private static PaymentGatewayFactory Criar(
        IReadOnlyDictionary<TipoProjeto, PaymentRoutingPolicy>? politicas = null) => new(
        politicas ?? PaymentRoutingPolicy.Padrao,
        [
            new GatewayFake(PaymentProvider.Asaas,
                MetodoPagamento.CartaoCredito, MetodoPagamento.Boleto, MetodoPagamento.Pix),
            new GatewayFake(PaymentProvider.Cora,
                MetodoPagamento.Boleto, MetodoPagamento.Pix)
        ]);

    [Theory]
    [InlineData(MetodoPagamento.CartaoCredito)]
    [InlineData(MetodoPagamento.Boleto)]
    public void Casamento_Roteia_Para_Asaas(MetodoPagamento metodo)
    {
        var gateway = Criar().Resolver(TipoProjeto.Casamento, metodo);

        gateway.Provider.Should().Be(PaymentProvider.Asaas);
    }

    [Theory]
    [InlineData(MetodoPagamento.Boleto)]
    [InlineData(MetodoPagamento.Pix)]
    public void Formatura_Roteia_Para_Cora(MetodoPagamento metodo)
    {
        var gateway = Criar().Resolver(TipoProjeto.Formatura, metodo);

        gateway.Provider.Should().Be(PaymentProvider.Cora);
    }

    [Fact]
    public void Casamento_Com_Pix_Lanca_DomainException()
    {
        var acao = () => Criar().Resolver(TipoProjeto.Casamento, MetodoPagamento.Pix);

        acao.Should().Throw<MetodoPagamentoNaoSuportadoException>()
            .Which.Codigo.Should().Be(MetodoPagamentoNaoSuportadoException.CodigoErro);
    }

    [Fact]
    public void Formatura_Com_Cartao_Lanca_DomainException()
    {
        var acao = () => Criar().Resolver(TipoProjeto.Formatura, MetodoPagamento.CartaoCredito);

        acao.Should().Throw<MetodoPagamentoNaoSuportadoException>()
            .Which.Metodo.Should().Be(MetodoPagamento.CartaoCredito);
    }

    [Fact]
    public void Casamento_Enviado_Para_Cora_Lanca_DomainException()
    {
        var acao = () => Criar().Resolver(TipoProjeto.Casamento, MetodoPagamento.Boleto, PaymentProvider.Cora);

        acao.Should().Throw<ProviderNaoPermitidoException>()
            .Which.Permitido.Should().Be(PaymentProvider.Asaas);
    }

    [Fact]
    public void Formatura_Enviada_Para_Asaas_Lanca_DomainException()
    {
        var acao = () => Criar().Resolver(TipoProjeto.Formatura, MetodoPagamento.Pix, PaymentProvider.Asaas);

        acao.Should().Throw<ProviderNaoPermitidoException>()
            .Which.Solicitado.Should().Be(PaymentProvider.Asaas);
    }

    [Fact]
    public void Provider_Errado_Tem_Precedencia_Sobre_Metodo_Errado()
    {
        var acao = () => Criar().Resolver(TipoProjeto.Casamento, MetodoPagamento.Pix, PaymentProvider.Cora);

        acao.Should().Throw<ProviderNaoPermitidoException>();
    }

    [Fact]
    public void Excecao_Carrega_Alternativas_Para_O_Front()
    {
        var acao = () => Criar().Resolver(TipoProjeto.Formatura, MetodoPagamento.CartaoCredito);

        var ex = acao.Should().Throw<MetodoPagamentoNaoSuportadoException>().Which;
        ex.Suportados.Should().BeEquivalentTo([MetodoPagamento.Boleto, MetodoPagamento.Pix]);
        ex.Detalhes["metodosSuportados"].Should().BeEquivalentTo(new[] { "Boleto", "Pix" });
        ex.Detalhes["tipoProjeto"].Should().Be("Formatura");
    }

    [Fact]
    public void Matriz_Padrao_Expoe_Exatamente_Os_Metodos_Contratados()
    {
        var router = Criar();

        router.MetodosSuportados(TipoProjeto.Casamento)
            .Should().BeEquivalentTo([MetodoPagamento.Boleto, MetodoPagamento.CartaoCredito]);
        router.MetodosSuportados(TipoProjeto.Formatura)
            .Should().BeEquivalentTo([MetodoPagamento.Boleto, MetodoPagamento.Pix]);

        router.ProviderDe(TipoProjeto.Casamento).Should().Be(PaymentProvider.Asaas);
        router.ProviderDe(TipoProjeto.Formatura).Should().Be(PaymentProvider.Cora);
    }

    [Fact]
    public void Nenhum_Cruzamento_De_Dominio_Escapa_Da_Matriz()
    {
        var router = Criar();

        foreach (var projeto in Enum.GetValues<TipoProjeto>())
            foreach (var metodo in Enum.GetValues<MetodoPagamento>())
            {
                var permitido = router.MetodosSuportados(projeto).Contains(metodo);
                var resolver = () => router.Resolver(projeto, metodo);

                if (permitido)
                    resolver().Provider.Should().Be(router.ProviderDe(projeto));
                else
                    resolver.Should().Throw<MetodoPagamentoNaoSuportadoException>();
            }
    }

    /*  Política liberando cartão para Formatura: a Cora não implementa,
        então a factory barra antes de qualquer chamada HTTP.  */
    [Fact]
    public void Politica_Invalida_Nao_Vaza_Para_O_PSP()
    {
        var politicas = new Dictionary<TipoProjeto, PaymentRoutingPolicy>
        {
            [TipoProjeto.Formatura] = new(PaymentProvider.Cora,
                new HashSet<MetodoPagamento> { MetodoPagamento.Boleto, MetodoPagamento.CartaoCredito })
        };

        var acao = () => Criar(politicas).Resolver(TipoProjeto.Formatura, MetodoPagamento.CartaoCredito);

        acao.Should().Throw<MetodoPagamentoNaoSuportadoException>()
            .Which.Suportados.Should().BeEquivalentTo([MetodoPagamento.Boleto]);
    }

    [Fact]
    public void Dominio_Sem_Politica_E_Erro_De_Configuracao_Nao_De_Negocio()
    {
        var politicas = new Dictionary<TipoProjeto, PaymentRoutingPolicy>
        {
            [TipoProjeto.Formatura] = PaymentRoutingPolicy.Padrao[TipoProjeto.Formatura]
        };

        var acao = () => Criar(politicas).Resolver(TipoProjeto.Casamento, MetodoPagamento.Boleto);

        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Gateway_Nao_Registrado_E_Erro_De_Configuracao()
    {
        var factory = new PaymentGatewayFactory(
            PaymentRoutingPolicy.Padrao,
            [new GatewayFake(PaymentProvider.Cora, MetodoPagamento.Boleto, MetodoPagamento.Pix)]);

        var acao = () => factory.Resolver(TipoProjeto.Casamento, MetodoPagamento.Boleto);

        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Provider_Duplicado_No_Container_Falha_Na_Construcao()
    {
        var acao = () => new PaymentGatewayFactory(
            PaymentRoutingPolicy.Padrao,
            [
                new GatewayFake(PaymentProvider.Asaas, MetodoPagamento.Boleto),
                new GatewayFake(PaymentProvider.Asaas, MetodoPagamento.Pix)
            ]);

        acao.Should().Throw<InvalidOperationException>();
    }
}
