using System.Text.Json;
using FormaturasFlow.Api.Payments;
using FluentAssertions;
using Xunit;

namespace FormaturasFlow.Api.UnitTests.Payments;

/*  Os JSONs abaixo são RESPOSTAS REAIS do ambiente de stage da Cora
    (matls-clients.api.stage.cora.com.br, set/2026), reduzidas aos campos
    que o adapter lê.  Foram mantidos verbatim de propósito: o valor deste
    teste está em travar o formato do provedor, e um payload "arrumado" à
    mão deixaria de detectar a mudança que quebra produção.  */
public class CoraPaymentGatewayTests
{
    private const string FaturaBoleto = """
    {
      "id": "inv_C0ntANAVQUKtFnhzLt791Wg",
      "status": "OPEN",
      "total_amount": 35000,
      "payment_options": {
        "bank_slip": {
          "barcode": "40399157900000350000000090485367000022132101",
          "digitable": "40390000079048536700800221321011915790000035000",
          "our_number": "04853670000221321",
          "registered": false,
          "url": "https://storage.googleapis.com/boleto-1fc3373a.pdf"
        }
      },
      "payments": []
    }
    """;

    /*  Numa cobrança Pix a Cora NÃO devolve `payment_options.pix`: o
        copia-e-cola vem na raiz e a url de bank_slip vira o PNG do QR.  */
    private const string FaturaPix = """
    {
      "id": "inv_RvwjjYAaSjyvPCTPq0ozGA",
      "status": "OPEN",
      "total_amount": 25000,
      "payment_options": {
        "bank_slip": {
          "barcode": null,
          "digitable": null,
          "our_number": null,
          "registered": false,
          "url": "https://storage.googleapis.com/cobranca-qrcode-1d756d3c.png"
        }
      },
      "payments": [],
      "pix": {
        "emv": "00020101021226830014br.gov.bcb.pix2561qrcode-h.cora.com.br/v1/cobv/5d19cd7f5204000053039865802BR5925TEST ACCOUNT COMPANY NAME6009SAO PAULO62070503***630455F8"
      }
    }
    """;

    private static JsonElement Json(string bruto) => JsonDocument.Parse(bruto).RootElement.Clone();

    [Fact]
    public void Boleto_Expoe_Linha_Digitavel_Codigo_De_Barras_E_Pdf()
    {
        var c = CoraPaymentGateway.Mapear(Json(FaturaBoleto), MetodoPagamento.Boleto);

        c.Provider.Should().Be(PaymentProvider.Cora);
        c.ChargeId.Should().Be("inv_C0ntANAVQUKtFnhzLt791Wg");
        c.Status.Should().Be("OPEN");
        c.BoletoUrl.Should().Be("https://storage.googleapis.com/boleto-1fc3373a.pdf");
        c.BoletoLinhaDigitavel.Should().Be("40390000079048536700800221321011915790000035000");
        c.BoletoCodigoBarras.Should().Be("40399157900000350000000090485367000022132101");
        c.PixCopiaCola.Should().BeNull();
    }

    /*  A regressão que este teste trava: ler o Pix de `payment_options.pix`
        (que não existe) devolvia copia-e-cola nulo e o formando ficava sem
        como pagar.  */
    [Fact]
    public void Pix_Le_Copia_E_Cola_Da_Raiz_Da_Fatura()
    {
        var c = CoraPaymentGateway.Mapear(Json(FaturaPix), MetodoPagamento.Pix);

        c.PixCopiaCola.Should().StartWith("00020101021226830014br.gov.bcb.pix");
        c.PixQrCodeUrl.Should().Be("https://storage.googleapis.com/cobranca-qrcode-1d756d3c.png");
        c.LinkPagamento.Should().Be(c.PixQrCodeUrl);
    }

    /*  Campos de boleto numa cobrança Pix vêm nulos da Cora; o adapter não
        pode "aproveitar" a url do QR como se fosse PDF de boleto.  */
    [Fact]
    public void Pix_Nao_Preenche_Campos_De_Boleto()
    {
        var c = CoraPaymentGateway.Mapear(Json(FaturaPix), MetodoPagamento.Pix);

        c.BoletoUrl.Should().BeNull();
        c.BoletoLinhaDigitavel.Should().BeNull();
        c.BoletoCodigoBarras.Should().BeNull();
    }

    [Theory]
    [InlineData(FaturaPix, MetodoPagamento.Pix)]
    [InlineData(FaturaBoleto, MetodoPagamento.Boleto)]
    public void Consulta_Sem_Metodo_Deduz_Pelo_Conteudo_Da_Fatura(string fatura, MetodoPagamento esperado)
    {
        CoraPaymentGateway.Mapear(Json(fatura), metodo: null).Metodo.Should().Be(esperado);
    }

    [Fact]
    public void Fatura_Sem_Campos_Conhecidos_Nao_Explode()
    {
        var c = CoraPaymentGateway.Mapear(Json("""{"id":"inv_x"}"""), MetodoPagamento.Boleto);

        c.ChargeId.Should().Be("inv_x");
        c.Status.Should().Be("OPEN");
        c.BoletoUrl.Should().BeNull();
    }

    /*  A Cora devolve 400 para Idempotency-Key fora do formato UUID, e a
        referência interna é "parcela-123".  */
    [Fact]
    public void Chave_De_Idempotencia_E_Uuid_Versao_5()
    {
        var chave = CoraPaymentGateway.ChaveIdempotencia("parcela-123");

        chave.Should().NotBe(Guid.Empty);
        chave.ToString().Should().MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-5[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
    }

    /*  Determinismo é o que evita cobrar o formando duas vezes quando a
        emissão é reenviada (retry, duplo clique, segundo pod).  */
    [Fact]
    public void Mesma_Referencia_Gera_Sempre_A_Mesma_Chave()
    {
        CoraPaymentGateway.ChaveIdempotencia("parcela-123")
            .Should().Be(CoraPaymentGateway.ChaveIdempotencia("parcela-123"));
    }

    [Fact]
    public void Referencias_Diferentes_Geram_Chaves_Diferentes()
    {
        CoraPaymentGateway.ChaveIdempotencia("parcela-123")
            .Should().NotBe(CoraPaymentGateway.ChaveIdempotencia("parcela-124"));
    }

    [Fact]
    public void Valor_Vira_Centavos_Com_Arredondamento_Explicito()
    {
        CoraPaymentGateway.Centavos(250.00m).Should().Be(25000);
        CoraPaymentGateway.Centavos(0.01m).Should().Be(1);

        /*  Meio centavo arredonda para cima (AwayFromZero), não para o par.  */
        CoraPaymentGateway.Centavos(1.005m).Should().Be(101);
    }

    /*  Sem a checagem, valor absurdo estourava int32 e virava 500 opaco em
        vez de erro de domínio legível.  */
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    [InlineData(21_474_837)]
    public void Valor_Invalido_Falha_Antes_De_Chamar_A_Cora(decimal valor)
    {
        var acao = () => CoraPaymentGateway.Centavos(valor);

        acao.Should().Throw<PaymentGatewayException>();
    }

    [Fact]
    public void Valor_Pago_Vem_Em_Reais_A_Partir_Dos_Centavos()
    {
        var fatura = Json("""{"id":"inv_x","status":"PAID","total_paid":15000}""");

        CoraPaymentGateway.Mapear(fatura, MetodoPagamento.Boleto).ValorPago.Should().Be(150.00m);
    }

    /*  Nulo é "o provedor não informou"; zero seria "emitida e não paga".  */
    [Fact]
    public void Fatura_Sem_Total_Pago_Deixa_Valor_Pago_Nulo()
    {
        CoraPaymentGateway.Mapear(Json("""{"id":"inv_x"}"""), MetodoPagamento.Boleto)
            .ValorPago.Should().BeNull();
    }

    /*  Corpo de erro do PSP pode trazer dados do pagador; não pode ir
        inteiro para o log.  */
    [Fact]
    public void Corpo_De_Erro_Vai_Truncado_Para_O_Log()
    {
        var resumo = CoraPaymentGateway.Resumir(new string('x', 5000));

        resumo.Length.Should().BeLessThan(350);
        resumo.Should().EndWith("[truncado]");
    }

    [Fact]
    public void Corpo_De_Erro_Curto_Passa_Intacto()
    {
        CoraPaymentGateway.Resumir("""{"erro":"invalid"}""").Should().Be("""{"erro":"invalid"}""");
    }

    [Fact]
    public void Cora_Nao_Processa_Cartao_De_Credito()
    {
        var opcoes = Microsoft.Extensions.Options.Options.Create(new CoraOptions());

        var gateway = new CoraPaymentGateway(
            new HttpClient(),
            opcoes,
            new CoraTokenProvider(
                new NoOpHttpClientFactory(),
                opcoes,
                new CoraCredentials(opcoes),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CoraTokenProvider>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CoraPaymentGateway>.Instance);

        gateway.Suporta(MetodoPagamento.Boleto).Should().BeTrue();
        gateway.Suporta(MetodoPagamento.Pix).Should().BeTrue();
        gateway.Suporta(MetodoPagamento.CartaoCredito).Should().BeFalse();
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
