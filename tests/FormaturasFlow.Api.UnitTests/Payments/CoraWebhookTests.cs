using System.Text.Json;
using FormaturasFlow.Api.Payments;
using FluentAssertions;
using Xunit;

namespace FormaturasFlow.Api.UnitTests.Payments;

/*  O webhook da Cora é tratado como aviso, não como verdade: basta extrair
    o id da fatura, porque o estado é reconsultado por mTLS.  Estes testes
    fixam justamente a tolerância a envelope — é o que impede que uma troca
    de formato do provedor derrube a baixa de parcelas em produção.  */
public class CoraWebhookTests
{
    private static JsonElement Json(string bruto) => JsonDocument.Parse(bruto).RootElement.Clone();

    [Theory]
    [InlineData("""{"id":"inv_abc123"}""")]
    [InlineData("""{"event":"invoice.paid","resource":{"id":"inv_abc123"}}""")]
    [InlineData("""{"event":"invoice.paid","data":{"id":"inv_abc123"}}""")]
    [InlineData("""{"invoice":{"id":"inv_abc123","status":"PAID"}}""")]
    [InlineData("""{"object":{"id":"inv_abc123"}}""")]
    [InlineData("""{"invoice_id":"inv_abc123"}""")]
    public void Extrai_Id_Da_Fatura_Em_Qualquer_Envelope_Conhecido(string corpo)
    {
        CoraWebhookEndpoints.ExtrairChargeId(Json(corpo)).Should().Be("inv_abc123");
    }

    /*  O id da fatura (`inv_`) tem precedência sobre o id do evento, que
        vive na raiz do mesmo corpo.  Confundir os dois faria a API
        consultar um id inexistente e nunca dar a parcela como paga.  */
    [Fact]
    public void Prefere_Id_Da_Fatura_Ao_Id_Do_Evento()
    {
        var corpo = Json("""{"id":"evt_999","event":"invoice.paid","resource":{"id":"inv_abc123"}}""");

        CoraWebhookEndpoints.ExtrairChargeId(corpo).Should().Be("inv_abc123");
    }

    [Theory]
    [InlineData("""{"event":"ping"}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    public void Corpo_Sem_Id_Reconhecivel_Devolve_Nulo(string corpo)
    {
        CoraWebhookEndpoints.ExtrairChargeId(Json(corpo)).Should().BeNull();
    }

    [Theory]
    [InlineData("PAID", true)]
    [InlineData("paid", true)]
    [InlineData("SETTLED", true)]
    [InlineData("OPEN", false)]
    [InlineData("LATE", false)]
    [InlineData("CANCELLED", false)]
    [InlineData("DRAFT", false)]
    public void Pago_Sai_Do_Status_Do_Provedor(string status, bool esperado)
    {
        var cobranca = new CobrancaCriada(
            PaymentProvider.Cora, "inv_abc123", MetodoPagamento.Pix, status);

        CoraWebhookEndpoints.Montar(cobranca).Pago.Should().Be(esperado);
    }

    /*  A regressão que este teste trava: `PAID_PARTIALLY` contado como
        quitado daria baixa numa parcela de R$ 1.000 que recebeu R$ 300, e o
        saldo nunca mais seria cobrado.  */
    [Fact]
    public void Pagamento_Parcial_Nao_Quita_A_Parcela()
    {
        var cobranca = new CobrancaCriada(
            PaymentProvider.Cora, "inv_abc123", MetodoPagamento.Boleto, "PAID_PARTIALLY",
            ValorPago: 300.00m);

        var r = CoraWebhookEndpoints.Montar(cobranca);

        r.Pago.Should().BeFalse();
        r.Parcial.Should().BeTrue();
        r.ValorPago.Should().Be(300.00m);
    }

    [Theory]
    [InlineData("PAID")]
    [InlineData("SETTLED")]
    public void Quitado_Nao_E_Marcado_Como_Parcial(string status)
    {
        var r = CoraWebhookEndpoints.Montar(
            new CobrancaCriada(PaymentProvider.Cora, "inv_abc123", MetodoPagamento.Pix, status));

        r.Pago.Should().BeTrue();
        r.Parcial.Should().BeFalse();
    }

    [Theory]
    [InlineData("OPEN")]
    [InlineData("CANCELLED")]
    public void Sem_Recebimento_Nao_E_Pago_Nem_Parcial(string status)
    {
        var r = CoraWebhookEndpoints.Montar(
            new CobrancaCriada(PaymentProvider.Cora, "inv_abc123", MetodoPagamento.Pix, status));

        r.Pago.Should().BeFalse();
        r.Parcial.Should().BeFalse();
    }

    [Fact]
    public void Resposta_Preserva_Dados_De_Pagamento_Do_Pix()
    {
        var cobranca = new CobrancaCriada(
            PaymentProvider.Cora, "inv_abc123", MetodoPagamento.Pix, "OPEN",
            LinkPagamento: "https://qr.png", PixCopiaCola: "000201...", PixQrCodeUrl: "https://qr.png");

        var r = CoraWebhookEndpoints.Montar(cobranca);

        r.Provider.Should().Be("Cora");
        r.Metodo.Should().Be("Pix");
        r.PixCopiaCola.Should().Be("000201...");
        r.PixQrCodeUrl.Should().Be("https://qr.png");
        r.Pago.Should().BeFalse();
    }

    [Fact]
    public void Resposta_Carrega_O_Valor_Pago_Para_A_Baixa_Da_Parcela()
    {
        var cobranca = new CobrancaCriada(
            PaymentProvider.Cora, "inv_abc123", MetodoPagamento.Boleto, "PAID",
            ValorPago: 350.00m);

        var r = CoraWebhookEndpoints.Montar(cobranca);

        r.Pago.Should().BeTrue();
        r.ValorPago.Should().Be(350.00m);
    }
}
