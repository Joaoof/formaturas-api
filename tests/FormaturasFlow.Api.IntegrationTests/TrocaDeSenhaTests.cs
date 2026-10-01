using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

/*  Troca e redefinição de senha.

    Não existia nenhuma das duas, e a falta pesava mais do que parece: o
    formando entra com o CPF como senha, e CPF não é segredo — circula em
    matrícula, lista de presença e grupo de turma.  Sem troca, qualquer um
    que soubesse o CPF entrava na área dele e via contrato, endereço e
    telefone.  */
[Collection("api")]
public class TrocaDeSenhaTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Senha = "SenhaForte1!";

    private async Task<(HttpClient Http, string Email)> RegistrarAsync(string email)
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync(email, Senha, "Pessoa Teste");
        http.WithToken(tok.AccessToken);
        return (http, email);
    }

    private async Task<HttpStatusCode> LogarAsync(string email, string senha)
    {
        var anon = factory.CreateClient();
        var resp = await anon.PostAsJsonAsync("/auth/login", new { email, password = senha });
        return resp.StatusCode;
    }

    [Fact]
    public async Task Troca_A_Propria_Senha_E_A_Nova_Passa_A_Valer()
    {
        var (http, email) = await RegistrarAsync("troca@exemplo.com");
        const string nova = "OutraSenha456@";

        var resp = await http.PostAsJsonAsync("/auth/trocar-senha",
            new { senhaAtual = Senha, novaSenha = nova });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LogarAsync(email, nova)).Should().Be(HttpStatusCode.OK);
    }

    /*  A senha antiga precisa deixar de funcionar, senão a troca não protege
        de nada.  */
    [Fact]
    public async Task Senha_Antiga_Deixa_De_Funcionar()
    {
        var (http, email) = await RegistrarAsync("antiga@exemplo.com");

        await http.PostAsJsonAsync("/auth/trocar-senha",
            new { senhaAtual = Senha, novaSenha = "OutraSenha456@" });

        (await LogarAsync(email, Senha)).Should().NotBe(HttpStatusCode.OK);
    }

    /*  Exigir a senha atual é o que evita que um token roubado vire tomada
        permanente da conta.  */
    [Fact]
    public async Task Senha_Atual_Errada_E_Recusada_Com_Codigo_Proprio()
    {
        var (http, email) = await RegistrarAsync("errada@exemplo.com");

        var resp = await http.PostAsJsonAsync("/auth/trocar-senha",
            new { senhaAtual = "ChutandoAqui1!", novaSenha = "OutraSenha456@" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadFromJsonAsync<ErroMin>())!.Codigo.Should().Be("SENHA_ATUAL_INCORRETA");

        /*  E a senha original segue valendo.  */
        (await LogarAsync(email, Senha)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Senha_Nova_Fraca_E_Recusada_Com_Codigo_Proprio()
    {
        var (http, _) = await RegistrarAsync("fraca@exemplo.com");

        var resp = await http.PostAsJsonAsync("/auth/trocar-senha",
            new { senhaAtual = Senha, novaSenha = "semdigito" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadFromJsonAsync<ErroMin>())!.Codigo.Should().Be("SENHA_NOVA_INVALIDA");
    }

    [Fact]
    public async Task Sem_Login_Nao_Troca_Senha()
    {
        var anon = factory.CreateClient();

        var resp = await anon.PostAsJsonAsync("/auth/trocar-senha",
            new { senhaAtual = Senha, novaSenha = "OutraSenha456@" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /*  O caso real de quem esqueceu a senha: sem isto, a única saída era
        alterar o banco à mão.  */
    [Fact]
    public async Task Admin_Redefine_A_Senha_De_Outro()
    {
        var (admin, _) = await RegistrarAsync("admin@senha.com");   /*  primeiro usuário = super_admin  */
        var (_, email) = await RegistrarAsync("esqueceu@exemplo.com");
        const string nova = "DefinidaPeloAdmin9!";

        var resp = await admin.PostAsJsonAsync("/auth/admin/redefinir-senha",
            new { email, novaSenha = nova });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LogarAsync(email, nova)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Formando_Nao_Redefine_Senha_De_Ninguem()
    {
        await RegistrarAsync("admin2@senha.com");
        var (formando, _) = await RegistrarAsync("aluno@exemplo.com");
        var (_, vitima) = await RegistrarAsync("vitima@exemplo.com");

        var resp = await formando.PostAsJsonAsync("/auth/admin/redefinir-senha",
            new { email = vitima, novaSenha = "TomandoAConta9!" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LogarAsync(vitima, Senha)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Redefinir_Senha_De_Usuario_Inexistente_Devolve_404()
    {
        var (admin, _) = await RegistrarAsync("admin3@senha.com");

        var resp = await admin.PostAsJsonAsync("/auth/admin/redefinir-senha",
            new { email = "ninguem@exemplo.com", novaSenha = "QualquerSenha9!" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private record ErroMin(string Codigo, string[] Erros);
}
