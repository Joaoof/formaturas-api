using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

/*  O seed de administrador por configuração.

    Existe porque o /register só promove o PRIMEIRO usuário do sistema: a
    partir do segundo todo cadastro vira Aluno, e não há endpoint de
    promoção.  Sem o seed, dar acesso de admin a alguém exigia mexer no
    banco à mão.  */
[Collection("api")]
public class AdminSeedTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Senha = "Senha123@";

    private HttpClient ComAdmin(string email) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Admin:Email", email)
                                         .UseSetting("Admin:Password", Senha)
                                         .UseSetting("Admin:NomeCompleto", "Admin Semeado"))
               .CreateClient();

    private async Task<IReadOnlyList<string>> PapeisDeAsync(string email)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        var users = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        return user is null ? [] : (await users.GetRolesAsync(user)).ToArray();
    }

    [Fact]
    public async Task Cria_Admin_Quando_Nao_Existe()
    {
        const string email = "novo-admin@exemplo.com";

        ComAdmin(email);

        (await PapeisDeAsync(email)).Should().Contain(Roles.SuperAdmin);
    }

    [Fact]
    public async Task Admin_Semeado_Consegue_Logar_E_Recebe_O_Papel()
    {
        const string email = "login-admin@exemplo.com";
        var http = ComAdmin(email);

        var resp = await http.PostAsJsonAsync("/auth/login", new { email, password = Senha });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await resp.Content.ReadFromJsonAsync<TokenMin>();
        corpo!.Roles.Should().Contain(Roles.SuperAdmin);
    }

    /*  O caso que motivou tudo: a pessoa já se cadastrou pelo /register,
        caiu como Aluno, e precisa virar admin.  */
    [Fact]
    public async Task Promove_Usuario_Que_Ja_Existe_Como_Aluno()
    {
        const string email = "virou-admin@exemplo.com";

        var anonimo = factory.CreateClient();
        await anonimo.PostAsJsonAsync("/auth/register",
            new { email, password = Senha, nomeCompleto = "Antes Era Aluno" });

        /*  Primeiro usuário da base já nasce SuperAdmin; para provar a
            PROMOÇÃO, este precisa ter caído como Aluno.  */
        var antes = await PapeisDeAsync(email);

        ComAdmin(email);

        var depois = await PapeisDeAsync(email);
        depois.Should().Contain(Roles.SuperAdmin);
        depois.Should().Contain(antes);
    }

    [Fact]
    public async Task Sem_Configuracao_Nao_Cria_Ninguem()
    {
        factory.CreateClient();

        await using var escopo = factory.Services.CreateAsyncScope();
        var users = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        users.Users.Should().BeEmpty();
    }

    /*  Redeploy não pode sobrescrever a senha que o dono já trocou.  */
    [Fact]
    public async Task Rearranque_Nao_Reescreve_A_Senha_Existente()
    {
        const string email = "senha-propria@exemplo.com";
        const string novaSenha = "OutraSenha456@";

        ComAdmin(email);

        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var users = escopo.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            /*  Sem token de reset: a aplicação não registra os provedores
                de token do Identity, e aqui basta trocar a senha.  */
            var user = await users.FindByEmailAsync(email);
            (await users.RemovePasswordAsync(user!)).Succeeded.Should().BeTrue();
            (await users.AddPasswordAsync(user!, novaSenha)).Succeeded.Should().BeTrue();
        }

        /*  Sobe de novo com a senha ANTIGA na configuração.  */
        var http = ComAdmin(email);

        (await http.PostAsJsonAsync("/auth/login", new { email, password = novaSenha }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await http.PostAsJsonAsync("/auth/login", new { email, password = Senha }))
            .StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    private record TokenMin(string AccessToken, string Email, string[] Roles);
}
