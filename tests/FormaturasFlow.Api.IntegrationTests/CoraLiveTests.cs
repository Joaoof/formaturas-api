using FormaturasFlow.Api.Payments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

/*  Teste CONTRA A CORA DE VERDADE (ambiente de stage).

    Fica desligado por padrão: sem CORA_CERT_PEM/CORA_KEY_PEM apontando para
    uma credencial válida, cada fato é ignorado.  Existe porque nenhum mock
    pega a classe de falha que mais dói aqui — mTLS recusado, contrato do
    provedor mudado, Idempotency-Key rejeitada — e essas só aparecem quando
    a chamada sai da máquina.

    Rodar:
      CORA_CERT_PEM=/caminho/certificate.pem \
      CORA_KEY_PEM=/caminho/private-key.key \
      dotnet test --filter FullyQualifiedName~CoraLiveTests
*/
public class CoraLiveTests
{
    private static string? CertPath => Environment.GetEnvironmentVariable("CORA_CERT_PEM");
    private static string? KeyPath  => Environment.GetEnvironmentVariable("CORA_KEY_PEM");

    private static bool Configurado =>
        !string.IsNullOrWhiteSpace(CertPath) && File.Exists(CertPath)
        && !string.IsNullOrWhiteSpace(KeyPath) && File.Exists(KeyPath);

    private const string MotivoSkip =
        "Defina CORA_CERT_PEM e CORA_KEY_PEM para rodar contra o stage da Cora.";

    private static (CoraPaymentGateway Gateway, CoraCredentials Credenciais) Montar()
    {
        var opcoes = Options.Create(new CoraOptions
        {
            Sandbox = true,
            CertificatePemPath = CertPath!,
            PrivateKeyPemPath = KeyPath!
        });

        var credenciais = new CoraCredentials(opcoes);
        var http = new HttpClient(new CoraHttpHandler(credenciais));

        var tokens = new CoraTokenProvider(
            new FabricaFixa(http), opcoes, credenciais, NullLogger<CoraTokenProvider>.Instance);

        return (new CoraPaymentGateway(http, opcoes, tokens, NullLogger<CoraPaymentGateway>.Instance), credenciais);
    }

    private sealed class FabricaFixa(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }

    private static CobrancaRequest Pedido(MetodoPagamento metodo, decimal valor) => new(
        Metodo: metodo,
        Valor: valor,
        Vencimento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
        Descricao: $"Parcela formatura ({metodo})",
        ReferenciaExterna: $"teste-{metodo}-{Guid.NewGuid():N}",
        Pagador: new PagadorInfo(
            Nome: "Joao Teste Formatura",
            Documento: "111.222.333-96",
            Email: "joaodeus400@gmail.com"));

    /*  O client_id do fluxo OAuth é o CN do certificado; se isto quebrar, o
        appsettings de produção provavelmente ficou com o id de stage.  */
    [SkippableFact]
    public void ClientId_Vem_Do_Certificado()
    {
        Skip.IfNot(Configurado, MotivoSkip);

        var (_, credenciais) = Montar();

        credenciais.Configurada.Should().BeTrue();
        credenciais.ClientId.Should().StartWith("int-");
        credenciais.Certificado!.HasPrivateKey.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Emite_Boleto_Com_Linha_Digitavel()
    {
        Skip.IfNot(Configurado, MotivoSkip);

        var (gateway, _) = Montar();

        var c = await gateway.CriarCobrancaAsync(Pedido(MetodoPagamento.Boleto, 350.00m));

        c.ChargeId.Should().StartWith("inv_");
        c.Status.Should().Be("OPEN");
        c.BoletoUrl.Should().NotBeNullOrWhiteSpace();
        c.BoletoLinhaDigitavel.Should().NotBeNullOrWhiteSpace();
        c.BoletoCodigoBarras.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Emite_Pix_Com_Copia_E_Cola()
    {
        Skip.IfNot(Configurado, MotivoSkip);

        var (gateway, _) = Montar();

        var c = await gateway.CriarCobrancaAsync(Pedido(MetodoPagamento.Pix, 250.00m));

        c.ChargeId.Should().StartWith("inv_");
        c.PixCopiaCola.Should().StartWith("00020101");
        c.PixCopiaCola.Should().Contain("br.gov.bcb.pix");
        c.PixQrCodeUrl.Should().NotBeNullOrWhiteSpace();
    }

    /*  A garantia que protege o bolso do formando: reenviar a MESMA
        referência não pode abrir uma segunda fatura.  */
    [SkippableFact]
    public async Task Mesma_Referencia_Nao_Duplica_A_Cobranca()
    {
        Skip.IfNot(Configurado, MotivoSkip);

        var (gateway, _) = Montar();
        var pedido = Pedido(MetodoPagamento.Boleto, 120.00m);

        var primeira = await gateway.CriarCobrancaAsync(pedido);
        var segunda  = await gateway.CriarCobrancaAsync(pedido);

        segunda.ChargeId.Should().Be(primeira.ChargeId);
    }

    [SkippableFact]
    public async Task Consulta_Devolve_A_Fatura_Emitida()
    {
        Skip.IfNot(Configurado, MotivoSkip);

        var (gateway, _) = Montar();

        var criada = await gateway.CriarCobrancaAsync(Pedido(MetodoPagamento.Pix, 75.50m));
        var lida = await gateway.ConsultarCobrancaAsync(criada.ChargeId);

        lida.ChargeId.Should().Be(criada.ChargeId);
        lida.Metodo.Should().Be(MetodoPagamento.Pix);
        lida.PixCopiaCola.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task Cartao_De_Credito_Falha_Antes_De_Qualquer_Chamada()
    {
        Skip.IfNot(Configurado, MotivoSkip);

        var (gateway, _) = Montar();

        var acao = async () => await gateway.CriarCobrancaAsync(Pedido(MetodoPagamento.CartaoCredito, 10m));

        await acao.Should().ThrowAsync<PaymentGatewayException>();
    }
}
