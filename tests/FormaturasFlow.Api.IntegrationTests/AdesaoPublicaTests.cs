using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.Endpoints;
using FormaturasFlow.Api.IntegrationTests.Infra;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

/*  Adesão pública: o caminho que o formando percorre sozinho, sem estar
    logado.  É o fluxo mais crítico do produto — se ele falhar pela metade,
    sobra aluno cadastrado sem contrato, ou contrato sem acesso.  */
[Collection("api")]
public class AdesaoPublicaTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cpf = "11122233396";

    private const string PngValido =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private async Task<Guid> CriarTurmaAsync()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@adesao.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        var resp = await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "Turma Adesao" });
        return (await resp.Content.ReadFromJsonAsync<IdMin>())!.Id;
    }

    private static object Corpo(Guid turmaId, string? assinatura, string cpf = Cpf) => new
    {
        turmaId,
        dadosPessoais = new
        {
            nomeCompleto = "Formando Adesao",
            cpf,
            rg = "1234567",
            telefone = "63999990000",
            whatsapp = "63999990000",
            email = "formando@exemplo.com",
            endereco = "Rua A, 100",
            cidade = "Araguaina",
            cep = "77800000"
        },
        pacote = "Pacote Ouro",
        valorTotal = 1000m,
        numParcelas = 2,
        diaVencimento = 10,
        autorizaImagem = true,
        textoContratoCompleto = "CLAUSULA PRIMEIRA: servicos fotograficos.",
        parcelas = new[]
        {
            new { numero = 1, valor = 500m, vencimento = "2026-11-10" },
            new { numero = 2, valor = 500m, vencimento = "2026-12-10" }
        },
        assinaturaImagem = assinatura
    };

    [Fact]
    public async Task Adesao_Com_Assinatura_Cria_Contrato_Ja_Assinado()
    {
        var turmaId = await CriarTurmaAsync();
        var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/api/v1/public/adesao", Corpo(turmaId, PngValido));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var r = await resp.Content.ReadFromJsonAsync<AdesaoMin>();

        r!.Assinado.Should().BeTrue();
        r.ContratoId.Should().NotBe(Guid.Empty);
        r.Cpf.Should().Be(Cpf);
    }

    /*  A assinatura é opcional no contrato da API; sem ela a adesão passa,
        mas o contrato precisa admitir que não está assinado.  */
    [Fact]
    public async Task Adesao_Sem_Assinatura_Deixa_Contrato_Pendente_De_Assinatura()
    {
        var turmaId = await CriarTurmaAsync();
        var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/api/v1/public/adesao", Corpo(turmaId, assinatura: null));

        (await resp.Content.ReadFromJsonAsync<AdesaoMin>())!.Assinado.Should().BeFalse();
    }

    /*  O que faltava antes: a adesão terminava sem criar o acesso, e o
        formando não conseguia entrar no painel que ele acabou de ganhar.  */
    [Fact]
    public async Task Formando_Consegue_Logar_Com_O_Cpf_Depois_Da_Adesao()
    {
        var turmaId = await CriarTurmaAsync();
        var http = factory.CreateClient();

        await http.PostAsJsonAsync("/api/v1/public/adesao", Corpo(turmaId, PngValido));

        var login = await http.PostAsJsonAsync("/auth/login", new
        {
            email = PublicEndpoints.EmailDoFormando(Cpf),
            password = Cpf
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tok = await login.Content.ReadFromJsonAsync<TokenMin>();
        tok!.Roles.Should().Contain("aluno");
    }

    [Fact]
    public async Task Formando_Logado_Enxerga_O_Proprio_Contrato()
    {
        var turmaId = await CriarTurmaAsync();
        var http = factory.CreateClient();

        await http.PostAsJsonAsync("/api/v1/public/adesao", Corpo(turmaId, PngValido));

        var login = await http.PostAsJsonAsync("/auth/login", new
        {
            email = PublicEndpoints.EmailDoFormando(Cpf),
            password = Cpf
        });
        var tok = await login.Content.ReadFromJsonAsync<TokenMin>();

        var comToken = factory.CreateClient();
        comToken.WithToken(tok!.AccessToken);

        var meus = await comToken.GetFromJsonAsync<AlunoMin[]>("/api/v1/alunos/me");

        meus.Should().HaveCount(1);
        meus![0].NomeCompleto.Should().Be("Formando Adesao");
    }

    /*  Refazer a adesão com o mesmo CPF não pode explodir por e-mail
        duplicado nem criar um segundo acesso.  */
    [Fact]
    public async Task Refazer_Adesao_Reaproveita_O_Acesso_Existente()
    {
        var turmaId = await CriarTurmaAsync();
        var http = factory.CreateClient();

        var primeira = await http.PostAsJsonAsync("/api/v1/public/adesao", Corpo(turmaId, PngValido));
        primeira.StatusCode.Should().Be(HttpStatusCode.OK);

        var segunda = await http.PostAsJsonAsync("/api/v1/public/adesao", Corpo(turmaId, PngValido));

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await http.PostAsJsonAsync("/auth/login", new
        {
            email = PublicEndpoints.EmailDoFormando(Cpf),
            password = Cpf
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cpf_Invalido_Nao_Cria_Nada()
    {
        var turmaId = await CriarTurmaAsync();
        var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/api/v1/public/adesao", Corpo(turmaId, PngValido, cpf: "123"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Assinatura_Fora_Do_Formato_Derruba_A_Adesao_Inteira()
    {
        var turmaId = await CriarTurmaAsync();
        var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/api/v1/public/adesao",
            Corpo(turmaId, "data:image/svg+xml;base64,PHN2Zy8+"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        /*  Transação: nada pode ter sobrado do aluno criado antes da falha.  */
        var login = await http.PostAsJsonAsync("/auth/login", new
        {
            email = PublicEndpoints.EmailDoFormando(Cpf),
            password = Cpf
        });
        login.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    private record IdMin(Guid Id);
    private record AdesaoMin(Guid AlunoId, string Nome, string Cpf, string LoginUsuario, Guid ContratoId, bool Assinado);
    private record TokenMin(string AccessToken, string Email, string[] Roles);
    private record AlunoMin(Guid Id, string NomeCompleto);
}
