using System.Text.Json;
using FormaturasFlow.Api.Payments;
using FluentAssertions;
using Xunit;

namespace FormaturasFlow.Api.UnitTests.Payments;

/*  Cadastro do endpoint de notificação na Cora.

    Os JSONs abaixo são RESPOSTAS REAIS do ambiente de stage
    (matls-clients.api.stage.cora.com.br, set/2026).  Mantidos verbatim: é o
    formato do provedor que estes testes existem para travar.  */
public class CoraEndpointsTests
{
    private const string EndpointCriado = """
    {
      "id": "end_4mznmCrxzVa1S3D9qvmSfg",
      "url": "https://api.exemplo.com.br/api/v1/pagamentos/webhooks/cora?secret=abc123",
      "resource": "invoice",
      "trigger": "paid",
      "connectionTimeout": 1000,
      "readTimeout": 2000,
      "includeResource": false,
      "expandable": false,
      "active": true
    }
    """;

    private static JsonElement Json(string bruto) => JsonDocument.Parse(bruto).RootElement.Clone();

    [Fact]
    public void Mapeia_O_Endpoint_Devolvido_Pela_Cora()
    {
        var e = CoraEndpointsClient.Mapear(Json(EndpointCriado));

        e.Id.Should().Be("end_4mznmCrxzVa1S3D9qvmSfg");
        e.Resource.Should().Be("invoice");
        e.Trigger.Should().Be("paid");
        e.Active.Should().BeTrue();
        e.IncludeResource.Should().BeFalse();
        e.ConnectionTimeout.Should().Be(1000);
        e.ReadTimeout.Should().Be(2000);
    }

    [Fact]
    public void Endpoint_Sem_Campos_Conhecidos_Nao_Explode()
    {
        var e = CoraEndpointsClient.Mapear(Json("""{"id":"end_x"}"""));

        e.Id.Should().Be("end_x");
        e.Active.Should().BeTrue();          /*  default seguro  */
        e.ConnectionTimeout.Should().BeNull();
    }

    /*  A URL cadastrada carrega o segredo na query.  Devolvê-la inteira na
        listagem vazaria esse segredo para qualquer um com acesso ao painel.  */
    [Fact]
    public void Listagem_Nao_Devolve_O_Segredo_Da_Query()
    {
        var limpa = CoraAdminEndpoints.SemQuery(
            "https://api.exemplo.com.br/api/v1/pagamentos/webhooks/cora?secret=SEGREDO-VAZANDO");

        limpa.Should().Be("https://api.exemplo.com.br/api/v1/pagamentos/webhooks/cora");
        limpa.Should().NotContain("SEGREDO-VAZANDO");
    }

    [Fact]
    public void Url_Invalida_Passa_Intacta_Em_Vez_De_Explodir()
    {
        CoraAdminEndpoints.SemQuery("nao-e-url").Should().Be("nao-e-url");
    }

    /*  A Cora não aceita cabeçalho customizado no aviso: o cadastro só tem
        url, recurso e gatilho.  Logo o segredo TEM de ir na query, senão o
        nosso webhook recusa tudo com 401.  */
    [Fact]
    public void Monta_A_Url_Do_Webhook_Com_O_Segredo_Na_Query()
    {
        var (url, codigo, erro) = CoraAdminEndpoints.UrlDoWebhook(new CoraOptions
        {
            WebhookUrlPublica = "https://api.exemplo.com.br",
            WebhookSecret = "s3gr3d0"
        });

        codigo.Should().BeNull();
        erro.Should().BeNull();
        url.Should().Be("https://api.exemplo.com.br/api/v1/pagamentos/webhooks/cora?secret=s3gr3d0");
    }

    [Fact]
    public void Barra_Sobrando_No_Endereco_Publico_Nao_Duplica_Na_Url()
    {
        var (url, _, _) = CoraAdminEndpoints.UrlDoWebhook(new CoraOptions
        {
            WebhookUrlPublica = "https://api.exemplo.com.br/",
            WebhookSecret = "s3gr3d0"
        });

        url.Should().NotContain("//api/v1");
    }

    /*  Segredo com caractere especial precisa sair escapado, senão a Cora
        cadastra uma URL que o nosso webhook não reconhece.  */
    [Fact]
    public void Segredo_Com_Caractere_Especial_Vai_Escapado()
    {
        var (url, _, _) = CoraAdminEndpoints.UrlDoWebhook(new CoraOptions
        {
            WebhookUrlPublica = "https://api.exemplo.com.br",
            WebhookSecret = "a+b/c=d&e"
        });

        url.Should().Contain("secret=a%2Bb%2Fc%3Dd%26e");
    }

    [Fact]
    public void Sem_Endereco_Publico_Explica_O_Que_Falta()
    {
        var (url, codigo, erro) = CoraAdminEndpoints.UrlDoWebhook(new CoraOptions
        {
            WebhookSecret = "s3gr3d0"
        });

        url.Should().BeNull();
        codigo.Should().Be("WEBHOOK_URL_NAO_CONFIGURADA");
        erro.Should().NotBeNullOrWhiteSpace();
    }

    /*  Cadastrar sem segredo criaria um endpoint que recebe aviso e devolve
        503 em todos — pior que não cadastrar, porque parece configurado.  */
    [Fact]
    public void Sem_Segredo_Recusa_Montar_A_Url()
    {
        var (url, codigo, _) = CoraAdminEndpoints.UrlDoWebhook(new CoraOptions
        {
            WebhookUrlPublica = "https://api.exemplo.com.br"
        });

        url.Should().BeNull();
        codigo.Should().Be("WEBHOOK_SECRET_NAO_CONFIGURADO");
    }

    [Fact]
    public void Recursos_Aceitos_Cobrem_O_Catalogo_Da_Cora()
    {
        CoraEndpointsClient.Recursos.Should().Contain(["invoice", "transfer", "payment", "*"]);
        CoraEndpointsClient.GatilhosDeFatura.Should().Contain(["paid", "canceled", "overdue", "*"]);
    }

    /*  Mesmo cadastro reenviado precisa gerar a MESMA chave, senão a Cora
        cria endpoint duplicado e o pagamento é avisado duas vezes.  */
    [Fact]
    public void Chave_De_Idempotencia_Do_Endpoint_E_Estavel()
    {
        var a = CoraPaymentGateway.ChaveIdempotencia("endpoint|https://x/y|invoice|*");
        var b = CoraPaymentGateway.ChaveIdempotencia("endpoint|https://x/y|invoice|*");
        var c = CoraPaymentGateway.ChaveIdempotencia("endpoint|https://x/y|invoice|paid");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
