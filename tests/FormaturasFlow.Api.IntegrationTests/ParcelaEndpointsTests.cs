using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

[Collection("api")]
public class ParcelaEndpointsTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient http, Guid parcelaId, decimal valor)> ArrangeContratoComUmaParcelaAsync()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "T" });
        var t = (await http.GetFromJsonAsync<TurmaMin[]>("/api/v1/turmas"))![0];

        var alunoResp = await http.PostAsJsonAsync("/api/v1/alunos", new { turmaId = t.Id, nomeCompleto = "M" });
        var aluno = await alunoResp.Content.ReadFromJsonAsync<AlunoMin>();

        var contratoResp = await http.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId = aluno!.Id,
            valorTotal = 500m,
            valorEntrada = 0m,
            numParcelas = 1,
            dataContrato = "2026-01-01",
            primeiroVencimento = "2026-02-05"
        });
        var contrato = await contratoResp.Content.ReadFromJsonAsync<ContratoDetalhe>();

        var parcelas = await http.GetFromJsonAsync<ParcelaMin[]>("/api/v1/parcelas");
        return (http, parcelas![0].Id, parcelas[0].Valor);
    }

    [Fact]
    public async Task Baixar_Marca_Como_Pago_Com_Valor_E_Data()
    {
        var (http, id, valor) = await ArrangeContratoComUmaParcelaAsync();

        var resp = await http.PostAsJsonAsync($"/api/v1/parcelas/{id}/baixar", new
        {
            valorPago = valor,
            dataPagamento = "2026-02-04",
            formaPagamento = "pix"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ParcelaFull>();
        body!.Status.Should().Be("Pago");
        body.ValorPago.Should().Be(valor);
    }

    [Fact]
    public async Task Baixar_Sem_ValorPago_Preenche_Com_Valor_Original()
    {
        var (http, id, valor) = await ArrangeContratoComUmaParcelaAsync();

        var resp = await http.PostAsJsonAsync($"/api/v1/parcelas/{id}/baixar", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ParcelaFull>();
        body!.Status.Should().Be("Pago");
        body.ValorPago.Should().Be(valor);
    }

    [Fact]
    public async Task Desfazer_Volta_Para_Pendente_E_Zera_ValorPago()
    {
        var (http, id, valor) = await ArrangeContratoComUmaParcelaAsync();
        await http.PostAsJsonAsync($"/api/v1/parcelas/{id}/baixar", new { valorPago = valor });

        var resp = await http.PostAsJsonAsync($"/api/v1/parcelas/{id}/desfazer", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ParcelaFull>();
        body!.Status.Should().Be("Pendente");
        body.ValorPago.Should().Be(0m);
        body.DataPagamento.Should().BeNull();
    }

    [Fact]
    public async Task Filtrar_Listagem_Por_Status()
    {
        var (http, id, valor) = await ArrangeContratoComUmaParcelaAsync();
        await http.PostAsJsonAsync($"/api/v1/parcelas/{id}/baixar", new { valorPago = valor });

        var pagas = await http.GetFromJsonAsync<ParcelaFull[]>("/api/v1/parcelas?status=pago");
        var pendentes = await http.GetFromJsonAsync<ParcelaFull[]>("/api/v1/parcelas?status=pendente");

        pagas.Should().ContainSingle();
        pendentes.Should().BeEmpty();
    }

    [Fact]
    public async Task Baixar_De_Parcela_Inexistente_Retorna_404()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@ex.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        var resp = await http.PostAsJsonAsync($"/api/v1/parcelas/{Guid.NewGuid()}/baixar", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private record TurmaMin(Guid Id, string Nome);
    private record AlunoMin(Guid Id, Guid TurmaId, string NomeCompleto);
    private record ContratoDetalhe(Guid Id);
    private record ParcelaMin(Guid Id, int Numero, decimal Valor);
    private record ParcelaFull(Guid Id, int Numero, decimal Valor, decimal ValorPago, string Status, string? DataPagamento);
}
