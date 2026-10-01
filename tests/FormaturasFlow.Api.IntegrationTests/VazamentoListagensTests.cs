using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

/*  Isolamento das listagens entre formandos.

    Encontrado sondando a API de produção com uma conta recém-criada: as
    listagens de alunos, contratos, parcelas e turmas exigiam apenas estar
    autenticado, sem papel.  Como /auth/register é PÚBLICO, a cadeia era:
    qualquer pessoa da internet cria uma conta e lê nome, CPF, RG, endereço e
    telefone de todos os formandos, além de todos os contratos e parcelas.

    Os endpoints de escrita já exigiam papel de equipe; só a leitura estava
    aberta — que é justamente o lado que expõe dado pessoal.  */
[Collection("api")]
public class VazamentoListagensTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Senha = "SenhaForte1!";

    private record Cenario(HttpClient Equipe, HttpClient Formando, HttpClient Estranho, Guid TurmaDoFormando);

    private async Task<Cenario> MontarAsync()
    {
        var equipe = factory.CreateClient();
        var tok = await equipe.RegisterAsync("admin@vaza.com", Senha, "Admin");
        equipe.WithToken(tok.AccessToken);

        /*  Duas turmas com um formando cada: é o que permite provar que um
            não vê o outro.  */
        var t1 = await CriarTurmaAsync(equipe, "Turma A");
        var t2 = await CriarTurmaAsync(equipe, "Turma B");

        await CriarFormandoComContratoAsync(equipe, t1, "Formando A", "52998224725");
        await CriarFormandoComContratoAsync(equipe, t2, "Formando B", "11144477735");

        return new Cenario(
            equipe,
            await LogarFormandoAsync("52998224725"),
            await RegistrarEstranhoAsync(),
            t1);
    }

    private static async Task<Guid> CriarTurmaAsync(HttpClient equipe, string nome)
    {
        var resp = await equipe.PostAsJsonAsync("/api/v1/turmas", new { nome });
        return (await resp.Content.ReadFromJsonAsync<IdMin>())!.Id;
    }

    private async Task CriarFormandoComContratoAsync(HttpClient equipe, Guid turmaId, string nome, string cpf)
    {
        var alunoResp = await equipe.PostAsJsonAsync("/api/v1/alunos",
            new { turmaId, nomeCompleto = nome, cpf, endereco = "Rua X, 1", whatsapp = "63900000000" });
        var aluno = await alunoResp.Content.ReadFromJsonAsync<IdMin>();

        await equipe.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId = aluno!.Id,
            pacote = "Pacote",
            valorTotal = 1000m,
            valorEntrada = 0m,
            numParcelas = 2,
            diaVencimento = 10,
            dataContrato = "2026-10-01",
            primeiroVencimento = "2026-11-10"
        });

        var email = $"{cpf}@formandos.local";
        var anon = factory.CreateClient();
        await anon.PostAsJsonAsync("/auth/register", new { email, password = cpf, nomeCompleto = nome });
        await equipe.PostAsJsonAsync($"/api/v1/alunos/{aluno.Id}/vincular-user", new { email });
    }

    private async Task<HttpClient> LogarFormandoAsync(string cpf)
    {
        var http = factory.CreateClient();
        var resp = await http.PostAsJsonAsync("/auth/login",
            new { email = $"{cpf}@formandos.local", password = cpf });
        var tok = await resp.Content.ReadFromJsonAsync<TokenMin>();
        http.WithToken(tok!.AccessToken);
        return http;
    }

    /*  Conta criada pelo /auth/register público, sem vínculo com formando
        algum: é o atacante do cenário real.  */
    private async Task<HttpClient> RegistrarEstranhoAsync()
    {
        var http = factory.CreateClient();
        var resp = await http.PostAsJsonAsync("/auth/register",
            new { email = "estranho@internet.com", password = Senha, nomeCompleto = "Estranho" });
        var tok = await resp.Content.ReadFromJsonAsync<TokenMin>();
        http.WithToken(tok!.AccessToken);
        return http;
    }

    [Fact]
    public async Task Conta_Criada_Na_Internet_Nao_Le_Dado_De_Ninguem()
    {
        var c = await MontarAsync();

        (await c.Estranho.GetFromJsonAsync<object[]>("/api/v1/alunos"))!.Should().BeEmpty();
        (await c.Estranho.GetFromJsonAsync<object[]>("/api/v1/contratos"))!.Should().BeEmpty();
        (await c.Estranho.GetFromJsonAsync<object[]>("/api/v1/parcelas"))!.Should().BeEmpty();
        (await c.Estranho.GetFromJsonAsync<object[]>("/api/v1/turmas"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task Formando_Ve_Apenas_O_Proprio_Cadastro()
    {
        var c = await MontarAsync();

        var alunos = await c.Formando.GetFromJsonAsync<AlunoMin[]>("/api/v1/alunos");

        alunos.Should().HaveCount(1);
        alunos![0].NomeCompleto.Should().Be("Formando A");
    }

    [Fact]
    public async Task Formando_Ve_Apenas_O_Proprio_Contrato()
    {
        var c = await MontarAsync();

        (await c.Formando.GetFromJsonAsync<object[]>("/api/v1/contratos"))!.Should().HaveCount(1);
    }

    /*  Trocar o alunoId na URL era a forma mais obvia de bisbilhotar.  */
    [Fact]
    public async Task Formando_Filtrando_Pelo_Aluno_De_Outro_Nao_Recebe_Nada()
    {
        var c = await MontarAsync();
        var todos = await c.Equipe.GetFromJsonAsync<AlunoMin[]>("/api/v1/alunos");
        var outro = todos!.First(a => a.NomeCompleto == "Formando B");

        var vazio = await c.Formando.GetFromJsonAsync<object[]>($"/api/v1/contratos?alunoId={outro.Id}");

        vazio!.Should().BeEmpty();
    }

    [Fact]
    public async Task Formando_Ve_Apenas_As_Proprias_Parcelas()
    {
        var c = await MontarAsync();

        /*  Duas parcelas do contrato dele, e nenhuma do outro.  */
        (await c.Formando.GetFromJsonAsync<object[]>("/api/v1/parcelas"))!.Should().HaveCount(2);
    }

    [Fact]
    public async Task Formando_Ve_Apenas_A_Propria_Turma()
    {
        var c = await MontarAsync();

        var turmas = await c.Formando.GetFromJsonAsync<TurmaMin[]>("/api/v1/turmas");

        turmas.Should().HaveCount(1);
        turmas![0].Id.Should().Be(c.TurmaDoFormando);
    }

    /*  A equipe continua vendo tudo: a correção não pode cegar o painel de
        gestão, que é justamente quem precisa da visão completa.  */
    [Fact]
    public async Task Equipe_Continua_Vendo_Tudo()
    {
        var c = await MontarAsync();

        (await c.Equipe.GetFromJsonAsync<object[]>("/api/v1/alunos"))!.Should().HaveCount(2);
        (await c.Equipe.GetFromJsonAsync<object[]>("/api/v1/contratos"))!.Should().HaveCount(2);
        (await c.Equipe.GetFromJsonAsync<object[]>("/api/v1/parcelas"))!.Should().HaveCount(4);
        (await c.Equipe.GetFromJsonAsync<object[]>("/api/v1/turmas"))!.Should().HaveCount(2);
    }

    /*  O painel do formando usa /alunos/me, que já era restrito; o teste
        existe para garantir que a correção não o quebrou.  */
    [Fact]
    public async Task Formando_Continua_Enxergando_Se_Mesmo_Em_Alunos_Me()
    {
        var c = await MontarAsync();

        var meus = await c.Formando.GetFromJsonAsync<AlunoMin[]>("/api/v1/alunos/me");

        meus.Should().HaveCount(1);
        meus![0].NomeCompleto.Should().Be("Formando A");
    }

    private record IdMin(Guid Id);
    private record AlunoMin(Guid Id, string NomeCompleto);
    private record TurmaMin(Guid Id, string Nome);
    private record TokenMin(string AccessToken);
}
