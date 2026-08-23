using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class ContratoEndpointsTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient http, Guid turmaId, Guid alunoId)> ArrangeAlunoAsync()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "T" });
        var turmas = await http.GetFromJsonAsync<TurmaMin[]>("/api/v1/turmas");
        var tId = turmas![0].Id;

        var alunoResp = await http.PostAsJsonAsync("/api/v1/alunos", new
        {
            turmaId = tId,
            nomeCompleto = "Maria",
            cpf = "12345678900"
        });
        var aluno = await alunoResp.Content.ReadFromJsonAsync<AlunoMin>();
        return (http, tId, aluno!.Id);
    }

    [Fact]
    public async Task Criar_Contrato_Rateia_Parcelas_Com_Resto_Na_Ultima()
    {
        var (http, _, alunoId) = await ArrangeAlunoAsync();

        var payload = new
        {
            alunoId,
            pacote = "Pacote Basico",
            valorTotal = 1000m,
            valorEntrada = 100m,
            numParcelas = 3,
            formaPagamento = "boleto",
            dataContrato = "2026-01-01",
            primeiroVencimento = "2026-02-05"
        };

        var resp = await http.PostAsJsonAsync("/api/v1/contratos", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var contratoId = (await resp.Content.ReadFromJsonAsync<ContratoMin>())!.Id;

        var det = await http.GetFromJsonAsync<ContratoDetalhe>($"/api/v1/contratos/{contratoId}");

        det!.Parcelas.Should().HaveCount(3);
        det.Parcelas.OrderBy(p => p.Numero).Select(p => p.Valor).Sum()
            .Should().Be(900m);
        det.Parcelas.Should().OnlyContain(p => p.Status == "Pendente");
    }

    [Fact]
    public async Task Criar_Contrato_Com_Num_Parcelas_Invalido_Retorna_400()
    {
        var (http, _, alunoId) = await ArrangeAlunoAsync();

        var resp = await http.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId,
            valorTotal = 100m,
            valorEntrada = 0m,
            numParcelas = 0,
            dataContrato = "2026-01-01",
            primeiroVencimento = "2026-02-05"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Criar_Contrato_Para_Aluno_Inexistente_Retorna_404()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        var resp = await http.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId = Guid.NewGuid(),
            valorTotal = 100m,
            valorEntrada = 0m,
            numParcelas = 1,
            dataContrato = "2026-01-01",
            primeiroVencimento = "2026-02-05"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Vencimentos_Sao_Mensais_A_Partir_Do_PrimeiroVencimento()
    {
        var (http, _, alunoId) = await ArrangeAlunoAsync();

        var create = await http.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId,
            valorTotal = 300m,
            valorEntrada = 0m,
            numParcelas = 3,
            dataContrato = "2026-01-01",
            primeiroVencimento = "2026-02-10"
        });
        var contratoId = (await create.Content.ReadFromJsonAsync<ContratoMin>())!.Id;
        var det = await http.GetFromJsonAsync<ContratoDetalhe>($"/api/v1/contratos/{contratoId}");

        var venc = det!.Parcelas.OrderBy(p => p.Numero).Select(p => DateOnly.Parse(p.Vencimento)).ToArray();
        venc.Should().Equal(new DateOnly(2026, 2, 10), new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 10));
    }

    private record TurmaMin(Guid Id, string Nome);
    private record AlunoMin(Guid Id, Guid TurmaId, string NomeCompleto);
    private record ContratoMin(Guid Id);
    private record ContratoDetalhe(Guid Id, ParcelaMin[] Parcelas);
    private record ParcelaMin(Guid Id, int Numero, decimal Valor, string Vencimento, string Status);
}
