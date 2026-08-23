using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class EfiWebhookTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Secret = "test-webhook-secret";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Webhook_Sem_Secret_Retorna_401()
    {
        var http = factory.CreateClient();
        var payload = new StringContent("""{ "evento": { "id": "evt1" } }""", Encoding.UTF8, "application/json");
        var resp = await http.PostAsync("/webhooks/efi", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_Com_Secret_Errado_Retorna_401()
    {
        var http = factory.CreateClient();
        var payload = new StringContent("""{ "evento": { "id": "evt1" } }""", Encoding.UTF8, "application/json");
        var resp = await http.PostAsync($"/webhooks/efi?secret=errado", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_Novo_Evento_Grava_E_Retorna_200()
    {
        var http = factory.CreateClient();
        var body = """{ "evento": { "id": "evt-1001" }, "notification": "pix.received" }""";

        var resp = await http.PostAsync($"/webhooks/efi?secret={Secret}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("ok").And.NotContain("duplicado");
    }

    [Fact]
    public async Task Webhook_Duplicado_E_Idempotente()
    {
        var http = factory.CreateClient();
        var body = """{ "evento": { "id": "evt-2002" }, "notification": "pix.received" }""";

        var first = await http.PostAsync($"/webhooks/efi?secret={Secret}",
            new StringContent(body, Encoding.UTF8, "application/json"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await http.PostAsync($"/webhooks/efi?secret={Secret}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("duplicado");
    }
}
