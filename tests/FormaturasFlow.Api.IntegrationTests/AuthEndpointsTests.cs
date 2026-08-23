using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class AuthEndpointsTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Primeiro_Register_Vira_Super_Admin()
    {
        var http = factory.CreateClient();

        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");

        tok.Email.Should().Be("admin@ex.com");
        tok.Roles.Should().Contain("super_admin");
        tok.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Segundo_Register_Vira_Aluno_Nao_Admin()
    {
        var http = factory.CreateClient();
        await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");

        var tok = await http.RegisterAsync("aluno@ex.com", "SenhaForte1!", "Aluno");

        tok.Roles.Should().Contain("aluno").And.NotContain("super_admin");
    }

    [Fact]
    public async Task Register_Com_Email_Duplicado_Retorna_400()
    {
        var http = factory.CreateClient();
        await http.RegisterAsync("dup@ex.com", "SenhaForte1!", "First");

        var resp = await http.PostAsJsonAsync("/auth/register", new
        {
            email = "dup@ex.com",
            password = "SenhaForte1!",
            nomeCompleto = "Second"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Valido_Retorna_Token_Com_Mesmas_Roles()
    {
        var http = factory.CreateClient();
        var registered = await http.RegisterAsync("user@ex.com", "SenhaForte1!", "User");

        var loggedIn = await http.LoginAsync("user@ex.com", "SenhaForte1!");

        loggedIn.Roles.Should().BeEquivalentTo(registered.Roles);
        loggedIn.Email.Should().Be(registered.Email);
    }

    [Fact]
    public async Task Login_Com_Senha_Errada_Retorna_401()
    {
        var http = factory.CreateClient();
        await http.RegisterAsync("user@ex.com", "SenhaForte1!", "User");

        var resp = await http.PostAsJsonAsync("/auth/login", new { email = "user@ex.com", password = "ErradaXpto" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Com_Email_Inexistente_Retorna_401()
    {
        var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/auth/login", new { email = "ninguem@ex.com", password = "qualquer" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Lockout_Depois_De_Cinco_Falhas_Consecutivas()
    {
        var http = factory.CreateClient();
        await http.RegisterAsync("locked@ex.com", "SenhaForte1!", "Locked");

        for (var i = 0; i < 5; i++)
            await http.PostAsJsonAsync("/auth/login", new { email = "locked@ex.com", password = "errada" });

        var resp = await http.PostAsJsonAsync("/auth/login", new { email = "locked@ex.com", password = "SenhaForte1!" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_Sem_Token_Retorna_401()
    {
        var http = factory.CreateClient();
        var resp = await http.GetAsync("/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_Com_Token_Valido_Retorna_Dados_Do_User()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("me@ex.com", "SenhaForte1!", "Me");

        http.WithToken(tok.AccessToken);
        var resp = await http.GetAsync("/auth/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("me@ex.com").And.Contain("super_admin");
    }
}
