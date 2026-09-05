using System.Net;
using System.Text;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class AsaasWebhookTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Token = "test-asaas-webhook-token";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Webhook_Sem_Header_Retorna_401()
    {
        var http = factory.CreateClient();
        var resp = await http.PostAsync("/webhooks/asaas", JsonBody("""{ "id": "evt_1" }"""));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_Com_Header_Errado_Retorna_401()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("asaas-access-token", "errado");
        var resp = await http.PostAsync("/webhooks/asaas", JsonBody("""{ "id": "evt_1" }"""));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_Novo_Evento_Retorna_200()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("asaas-access-token", Token);
        var resp = await http.PostAsync("/webhooks/asaas",
            JsonBody("""{ "id": "evt_new_1", "event": "PAYMENT_RECEIVED", "payment": { "id": "pay_1", "status": "RECEIVED" } }"""));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("ok").And.NotContain("duplicado");
    }

    [Fact]
    public async Task Webhook_Duplicado_E_Idempotente()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("asaas-access-token", Token);
        var body = """{ "id": "evt_dup_1", "event": "PAYMENT_RECEIVED", "payment": { "id": "pay_2", "status": "RECEIVED" } }""";

        var first = await http.PostAsync("/webhooks/asaas", JsonBody(body));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await http.PostAsync("/webhooks/asaas", JsonBody(body));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("duplicado");
    }
}
