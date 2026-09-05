using System.Net;
using System.Text;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class CoraWebhookTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Token = "test-cora-webhook-token";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Webhook_Sem_Signature_Retorna_401()
    {
        var http = factory.CreateClient();
        var resp = await http.PostAsync("/webhooks/cora", JsonBody("""{ "eventId": "e1" }"""));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_Com_Signature_Errada_Retorna_401()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("x-cora-signature", "errada");
        var resp = await http.PostAsync("/webhooks/cora", JsonBody("""{ "eventId": "e1" }"""));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_Novo_Evento_Retorna_200()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("x-cora-signature", Token);
        var resp = await http.PostAsync("/webhooks/cora",
            JsonBody("""{ "eventId": "cora_evt_1", "event": "invoice.paid", "invoice": { "id": "inv_1", "status": "PAID" } }"""));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("ok").And.NotContain("duplicado");
    }

    [Fact]
    public async Task Webhook_Duplicado_E_Idempotente()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("x-cora-signature", Token);
        var body = """{ "eventId": "cora_dup_1", "event": "invoice.paid", "invoice": { "id": "inv_2", "status": "PAID" } }""";

        var first = await http.PostAsync("/webhooks/cora", JsonBody(body));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await http.PostAsync("/webhooks/cora", JsonBody(body));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("duplicado");
    }
}
