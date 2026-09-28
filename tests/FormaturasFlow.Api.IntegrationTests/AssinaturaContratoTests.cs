using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.Domain;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

/*  Assinatura eletrônica do contrato.

    O que estes testes protegem não é a rubrica em si — é o vínculo entre a
    rubrica e o TEXTO assinado.  Sem esse vínculo, a assinatura vira enfeite:
    alguém edita as cláusulas depois e o documento continua "assinado".  */
[Collection("api")]
public class AssinaturaContratoTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /*  PNG 1x1 real, que é o formato que o canvas do navegador produz.  */
    private const string PngValido =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private async Task<(HttpClient Http, Guid ContratoId)> ArranjarContratoAsync(string texto = "CLAUSULA PRIMEIRA: teste.")
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@assinatura.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        var turmaResp = await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "Turma Assinatura" });
        var turma = await turmaResp.Content.ReadFromJsonAsync<IdMin>();

        var alunoResp = await http.PostAsJsonAsync("/api/v1/alunos",
            new { turmaId = turma!.Id, nomeCompleto = "Formando Teste", cpf = "11122233396" });
        var aluno = await alunoResp.Content.ReadFromJsonAsync<IdMin>();

        var contratoResp = await http.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId = aluno!.Id,
            pacote = "Pacote Ouro",
            valorTotal = 1000m,
            valorEntrada = 0m,
            numParcelas = 2,
            diaVencimento = 10,
            dataContrato = "2026-01-01",
            primeiroVencimento = "2026-02-10"
        });
        contratoResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var contrato = await contratoResp.Content.ReadFromJsonAsync<IdMin>();

        await http.PutAsJsonAsync($"/api/v1/contratos/{contrato!.Id}", new
        {
            pacote = "Pacote Ouro",
            valorTotal = 1000m,
            valorEntrada = 0m,
            numParcelas = 2,
            diaVencimento = 10,
            textoContrato = texto
        });

        return (http, contrato.Id);
    }

    private static HttpContent Assinatura(string imagem = PngValido) =>
        JsonContent.Create(new { imagem, nome = "Formando Teste", cpf = "111.222.333-96" });

    [Fact]
    public async Task Assina_E_Grava_A_Trilha_De_Auditoria()
    {
        var (http, id) = await ArranjarContratoAsync();

        var resp = await http.PostAsync($"/api/v1/contratos/{id}/assinar", Assinatura());

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var a = await resp.Content.ReadFromJsonAsync<AssinaturaMin>();

        a!.Assinado.Should().BeTrue();
        a.TextoIntacto.Should().BeTrue();
        a.AssinadoEm.Should().NotBeNull();
        a.HashDocumento.Should().NotBeNullOrWhiteSpace();
        a.AssinanteNome.Should().Be("Formando Teste");
        a.AssinanteCpf.Should().Be("11122233396");
    }

    /*  A regra que dá sentido a tudo: contrato assinado não muda de texto.  */
    [Fact]
    public async Task Texto_De_Contrato_Assinado_Nao_Pode_Ser_Alterado()
    {
        var (http, id) = await ArranjarContratoAsync();
        await http.PostAsync($"/api/v1/contratos/{id}/assinar", Assinatura());

        var resp = await http.PutAsJsonAsync($"/api/v1/contratos/{id}", new
        {
            pacote = "Pacote Ouro",
            valorTotal = 1000m,
            valorEntrada = 0m,
            numParcelas = 2,
            diaVencimento = 10,
            textoContrato = "CLAUSULA PRIMEIRA: texto trocado depois do aceite."
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var a = await http.GetFromJsonAsync<AssinaturaMin>($"/api/v1/contratos/{id}/assinatura");
        a!.TextoIntacto.Should().BeTrue();
    }

    /*  Dados financeiros ainda podem ser corrigidos; o que trava é o teor.  */
    [Fact]
    public async Task Contrato_Assinado_Ainda_Aceita_Ajuste_Que_Nao_Seja_O_Texto()
    {
        var (http, id) = await ArranjarContratoAsync();
        await http.PostAsync($"/api/v1/contratos/{id}/assinar", Assinatura());

        var resp = await http.PutAsJsonAsync($"/api/v1/contratos/{id}", new
        {
            pacote = "Pacote Diamante",
            valorTotal = 1000m,
            valorEntrada = 0m,
            numParcelas = 2,
            diaVencimento = 15
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nao_Assina_Duas_Vezes()
    {
        var (http, id) = await ArranjarContratoAsync();
        await http.PostAsync($"/api/v1/contratos/{id}/assinar", Assinatura());

        var resp = await http.PostAsync($"/api/v1/contratos/{id}/assinar", Assinatura());

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-data-url")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    [InlineData("data:image/png;base64,???")]
    public async Task Recusa_Assinatura_Fora_Do_Formato(string imagem)
    {
        var (http, id) = await ArranjarContratoAsync();

        var resp = await http.PostAsync($"/api/v1/contratos/{id}/assinar", Assinatura(imagem));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Contrato_Sem_Assinatura_Nao_Aparece_Como_Assinado()
    {
        var (http, id) = await ArranjarContratoAsync();

        var a = await http.GetFromJsonAsync<AssinaturaMin>($"/api/v1/contratos/{id}/assinatura");

        a!.Assinado.Should().BeFalse();
        a.AssinadoEm.Should().BeNull();

        /*  Sem assinatura não há o que violar: íntegro por vacuidade.  */
        a.TextoIntacto.Should().BeTrue();
    }

    /*  O hash precisa sobreviver a diferença de fim de linha, senão o
        navegador (CRLF) e o banco (LF) discordariam e todo contrato
        apareceria como adulterado.  */
    [Fact]
    public void Hash_Ignora_Diferenca_De_Fim_De_Linha()
    {
        AssinaturaContrato.Hash("linha 1\r\nlinha 2")
            .Should().Be(AssinaturaContrato.Hash("linha 1\nlinha 2"));
    }

    [Fact]
    public void Hash_Muda_Quando_O_Teor_Muda()
    {
        AssinaturaContrato.Hash("CLAUSULA: paga 10")
            .Should().NotBe(AssinaturaContrato.Hash("CLAUSULA: paga 100"));
    }

    /*  Adulteração detectada: o texto é trocado por baixo, direto no banco,
        e a verificação precisa acusar.  */
    [Fact]
    public async Task Texto_Trocado_Por_Fora_Acusa_Perda_De_Integridade()
    {
        var (http, id) = await ArranjarContratoAsync();
        await http.PostAsync($"/api/v1/contratos/{id}/assinar", Assinatura());

        await using (var escopo = factory.Services.CreateAsyncScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<FormaturasFlow.Api.Data.AppDbContext>();
            var c = await db.Contratos.FindAsync(id);
            c!.TextoContrato = "CLAUSULA PRIMEIRA: adulterada direto no banco.";
            await db.SaveChangesAsync();
        }

        var a = await http.GetFromJsonAsync<AssinaturaMin>($"/api/v1/contratos/{id}/assinatura");

        a!.Assinado.Should().BeTrue();
        a.TextoIntacto.Should().BeFalse();
    }

    private record IdMin(Guid Id);

    private record AssinaturaMin(
        Guid ContratoId,
        bool Assinado,
        bool TextoIntacto,
        string? AssinanteNome,
        string? AssinanteCpf,
        DateTimeOffset? AssinadoEm,
        string? AssinadoIp,
        string? AssinadoUserAgent,
        string? HashDocumento,
        string? AssinaturaImagem);
}
