using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class AlunoEndpointsTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient http, Guid turmaId)> AutenticadoComTurmaAsync()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);
        await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "T" });
        var turmas = await http.GetFromJsonAsync<TurmaMin[]>("/api/v1/turmas");
        return (http, turmas![0].Id);
    }

    [Fact]
    public async Task Criar_Aluno_Em_Turma_Valida_Retorna_201()
    {
        var (http, turmaId) = await AutenticadoComTurmaAsync();

        var resp = await http.PostAsJsonAsync("/api/v1/alunos", new
        {
            turmaId,
            nomeCompleto = "Ana",
            cpf = "11122233344",
            email = "ana@ex.com"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Criar_Aluno_Em_Turma_Inexistente_Retorna_404()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        var resp = await http.PostAsJsonAsync("/api/v1/alunos", new
        {
            turmaId = Guid.NewGuid(),
            nomeCompleto = "Ana"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Listar_Filtra_Por_TurmaId()
    {
        var (http, turmaId) = await AutenticadoComTurmaAsync();
        await http.PostAsJsonAsync("/api/v1/alunos", new { turmaId, nomeCompleto = "A" });
        await http.PostAsJsonAsync("/api/v1/alunos", new { turmaId, nomeCompleto = "B" });

        var todos = await http.GetFromJsonAsync<AlunoDto[]>("/api/v1/alunos");
        var da = await http.GetFromJsonAsync<AlunoDto[]>($"/api/v1/alunos?turmaId={turmaId}");
        var doutra = await http.GetFromJsonAsync<AlunoDto[]>($"/api/v1/alunos?turmaId={Guid.NewGuid()}");

        todos.Should().HaveCount(2);
        da.Should().HaveCount(2);
        doutra.Should().BeEmpty();
    }

    [Fact]
    public async Task Atualizar_Aluno_Retorna_200()
    {
        var (http, turmaId) = await AutenticadoComTurmaAsync();
        var create = await http.PostAsJsonAsync("/api/v1/alunos", new { turmaId, nomeCompleto = "Antigo" });
        var aluno = (await http.GetFromJsonAsync<AlunoDto[]>("/api/v1/alunos"))![0];

        var upd = await http.PutAsJsonAsync($"/api/v1/alunos/{aluno.Id}", new
        {
            nomeCompleto = "Novo",
            email = "novo@ex.com"
        });

        upd.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private record TurmaMin(Guid Id, string Nome);
    private record AlunoDto(Guid Id, Guid TurmaId, string NomeCompleto, string? Email);
}
