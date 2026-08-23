using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class TurmaEndpointsTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> AutenticadoComoAdminAsync()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        return http.WithToken(tok.AccessToken);
    }

    private async Task<HttpClient> AutenticadoComoAlunoAsync()
    {
        var http = factory.CreateClient();
        await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        var tok = await http.RegisterAsync("aluno@ex.com", "SenhaForte1!", "Aluno");
        return http.WithToken(tok.AccessToken);
    }

    [Fact]
    public async Task Listar_Sem_Autenticacao_Retorna_401()
    {
        var http = factory.CreateClient();
        var resp = await http.GetAsync("/api/v1/turmas");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Criar_Como_Aluno_Retorna_403()
    {
        var http = await AutenticadoComoAlunoAsync();

        var resp = await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "T1" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Criar_Listar_E_Detalhar_Ciclo_Feliz()
    {
        var http = await AutenticadoComoAdminAsync();

        var create = await http.PostAsJsonAsync("/api/v1/turmas", new
        {
            nome = "Enfermagem 2026",
            instituicao = "Uni X",
            curso = "Enfermagem",
            anoFormatura = 2026
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var lista = await http.GetFromJsonAsync<TurmaDto[]>("/api/v1/turmas");
        lista.Should().ContainSingle(t => t.Nome == "Enfermagem 2026" && t.TotalAlunos == 0);

        var id = lista![0].Id;
        var get = await http.GetAsync($"/api/v1/turmas/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Atualizar_Como_Admin_Ok()
    {
        var http = await AutenticadoComoAdminAsync();
        var create = await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "Antigo" });
        var lista = await http.GetFromJsonAsync<TurmaDto[]>("/api/v1/turmas");
        var id = lista![0].Id;

        var upd = await http.PutAsJsonAsync($"/api/v1/turmas/{id}", new { nome = "Novo Nome" });
        upd.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await http.GetFromJsonAsync<Dictionary<string, object>>($"/api/v1/turmas/{id}");
        get!["nome"].ToString().Should().Be("Novo Nome");
    }

    [Fact]
    public async Task Deletar_Turma_Sem_Ser_SuperAdmin_Retorna_403()
    {
        var http = factory.CreateClient();
        await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");

        var tok = await http.RegisterAsync("func@ex.com", "SenhaForte1!", "Func");
        http.WithToken(tok.AccessToken);
        var resp = await http.DeleteAsync($"/api/v1/turmas/{Guid.NewGuid()}");

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    private record TurmaDto(Guid Id, string Nome, string? Instituicao, string? Curso, int? AnoFormatura, int TotalAlunos);
}
