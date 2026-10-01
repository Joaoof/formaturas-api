using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FormaturasFlow.Api.IntegrationTests.Infra;
using FormaturasFlow.Api.Payments;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FormaturasFlow.Api.IntegrationTests;

/*  Quem pode emitir cobrança de qual parcela.

    Descoberto clicando no painel do formando: o botão "Pagar" aparecia nas
    parcelas dele, ele clicava e levava 403, porque o endpoint exigia papel de
    equipe.  Liberar para qualquer autenticado consertaria o 403 e abriria um
    buraco pior — formando emitindo cobrança na parcela de outro.  Estes
    testes travam as duas pontas.  */
[Collection("api")]
public class CobrancaDoFormandoTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /*  Dublê do PSP: o que está sob teste é a autorização, não o HTTP com a
        Cora (isso o CoraLiveTests cobre contra o provedor real).  */
    private sealed class GatewayFake : IPaymentGateway
    {
        public PaymentProvider Provider => PaymentProvider.Cora;

        public bool Suporta(MetodoPagamento metodo) => metodo is MetodoPagamento.Boleto or MetodoPagamento.Pix;

        public Task<CobrancaCriada> CriarCobrancaAsync(CobrancaRequest req, CancellationToken ct = default) =>
            Task.FromResult(new CobrancaCriada(
                Provider, $"inv_fake_{Guid.NewGuid():N}"[..20], req.Metodo, "OPEN",
                PixCopiaCola: "00020101-fake", BoletoLinhaDigitavel: "40390000-fake"));
    }

    private HttpClient ComPspFake() =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IPaymentGateway>();
            s.AddTransient<IPaymentGateway, GatewayFake>();
            s.AddTransient<IPaymentGateway, AsaasPaymentGateway>();
        })).CreateClient();

    private record Cenario(
        HttpClient Equipe,
        HttpClient Dono,
        HttpClient Outro,
        Guid ParcelaDoDono,
        Guid ParcelaDoOutro,
        decimal Valor);

    private async Task<Cenario> MontarAsync()
    {
        var equipe = ComPspFake();
        var tok = await equipe.RegisterAsync("admin@cobranca.com", "SenhaForte1!", "Admin");
        equipe.WithToken(tok.AccessToken);

        var turmaResp = await equipe.PostAsJsonAsync("/api/v1/turmas", new { nome = "Turma Cobranca" });
        var turma = await turmaResp.Content.ReadFromJsonAsync<IdMin>();

        var (parcelaA, cpfA) = await CriarFormandoComParcelaAsync(equipe, turma!.Id, "Formando A", "52998224725");
        var (parcelaB, cpfB) = await CriarFormandoComParcelaAsync(equipe, turma.Id, "Formando B", "11144477735");

        return new Cenario(
            equipe,
            await LogarFormandoAsync(cpfA),
            await LogarFormandoAsync(cpfB),
            parcelaA,
            parcelaB,
            600m);
    }

    private async Task<(Guid ParcelaId, string Cpf)> CriarFormandoComParcelaAsync(
        HttpClient equipe, Guid turmaId, string nome, string cpf)
    {
        var alunoResp = await equipe.PostAsJsonAsync("/api/v1/alunos",
            new { turmaId, nomeCompleto = nome, cpf });
        var aluno = await alunoResp.Content.ReadFromJsonAsync<IdMin>();

        await equipe.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId = aluno!.Id,
            pacote = "Pacote Ouro",
            valorTotal = 1200m,
            valorEntrada = 0m,
            numParcelas = 2,
            diaVencimento = 10,
            dataContrato = "2026-10-01",
            primeiroVencimento = "2026-11-10"
        });

        /*  O acesso do formando é o e-mail derivado do CPF; a vinculação é o
            que /alunos/me e a regra de posse usam para casar os dois.  */
        var email = $"{cpf}@formandos.local";
        var anon = factory.CreateClient();
        await anon.PostAsJsonAsync("/auth/register", new { email, password = cpf, nomeCompleto = nome });
        await equipe.PostAsJsonAsync($"/api/v1/alunos/{aluno.Id}/vincular-user", new { email });

        /*  A parcela DESTE aluno: a listagem traz de todos, e casar pelo
            contrato dele é o que evita um teste passar olhando a parcela do
            outro formando.  */
        var parcelas = await equipe.GetFromJsonAsync<ParcelaMin[]>("/api/v1/parcelas");
        var contratos = await equipe.GetFromJsonAsync<ContratoMin[]>($"/api/v1/contratos?alunoId={aluno.Id}");
        var doAluno = parcelas!.First(p => p.ContratoId == contratos![0].Id);

        return (doAluno.Id, cpf);
    }

    private async Task<HttpClient> LogarFormandoAsync(string cpf)
    {
        var http = ComPspFake();
        var resp = await http.PostAsJsonAsync("/auth/login",
            new { email = $"{cpf}@formandos.local", password = cpf });
        var tok = await resp.Content.ReadFromJsonAsync<TokenMin>();
        http.WithToken(tok!.AccessToken);
        return http;
    }

    private static object Corpo(Guid? parcelaId, decimal valor) => new
    {
        tipoProjeto = "Formatura",
        metodo = "Pix",
        valor,
        vencimento = "2026-11-10",
        descricao = "Parcela 1/2",
        referenciaExterna = $"parcela-{parcelaId?.ToString() ?? Guid.NewGuid().ToString()}",
        pagador = new { nome = "Formando", documento = "52998224725", email = "f@exemplo.com" },
        parcelaId
    };

    /*  O caso que estava quebrado: o formando pagando a própria parcela.  */
    [Fact]
    public async Task Formando_Emite_Cobranca_Da_Propria_Parcela()
    {
        var c = await MontarAsync();

        var resp = await c.Dono.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(c.ParcelaDoDono, c.Valor));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cobranca = await resp.Content.ReadFromJsonAsync<CobrancaMin>();
        cobranca!.ChargeId.Should().StartWith("inv_");
    }

    /*  E o buraco que não pode abrir junto.  */
    [Fact]
    public async Task Formando_Nao_Emite_Cobranca_Da_Parcela_De_Outro()
    {
        var c = await MontarAsync();

        var resp = await c.Dono.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(c.ParcelaDoOutro, c.Valor));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /*  Sem parcela não há posse para conferir; aceitar deixaria qualquer
        aluno criar cobrança solta na conta da empresa.  */
    [Fact]
    public async Task Formando_Sem_Informar_Parcela_E_Recusado()
    {
        var c = await MontarAsync();

        var resp = await c.Dono.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(null, c.Valor));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Equipe_Emite_Para_Qualquer_Parcela()
    {
        var c = await MontarAsync();

        var daA = await c.Equipe.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(c.ParcelaDoDono, c.Valor));
        var daB = await c.Equipe.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(c.ParcelaDoOutro, c.Valor));

        daA.StatusCode.Should().Be(HttpStatusCode.OK);
        daB.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /*  Equipe segue podendo emitir sem vínculo (cobrança avulsa).  */
    [Fact]
    public async Task Equipe_Emite_Sem_Parcela()
    {
        var c = await MontarAsync();

        var resp = await c.Equipe.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(null, c.Valor));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sem_Autenticacao_Continua_Barrado()
    {
        var c = await MontarAsync();
        var anon = factory.CreateClient();

        var resp = await anon.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(c.ParcelaDoDono, c.Valor));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Emissao_Do_Formando_Grava_O_Vinculo_Na_Parcela()
    {
        var c = await MontarAsync();

        await c.Dono.PostAsJsonAsync("/api/v1/pagamentos/cobrancas", Corpo(c.ParcelaDoDono, c.Valor));

        await using var escopo = factory.Services.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<FormaturasFlow.Api.Data.AppDbContext>();
        var parcela = await db.Parcelas.FindAsync(c.ParcelaDoDono);

        /*  Sem este vínculo o webhook não acha o que baixar quando o
            formando pagar.  */
        parcela!.PspChargeId.Should().StartWith("inv_");
        parcela.PspProvider.Should().Be("cora");
    }

    [Fact]
    public void Cpf_Do_Email_Do_Formando_E_Extraido()
    {
        DonoDaParcela.CpfDoEmail("52998224725@formandos.local").Should().Be("52998224725");
        DonoDaParcela.CpfDoEmail("admin@empresa.com").Should().BeNull();
        DonoDaParcela.CpfDoEmail(null).Should().BeNull();
        DonoDaParcela.CpfDoEmail("123@formandos.local").Should().BeNull();
    }

    private record IdMin(Guid Id);
    private record ContratoMin(Guid Id);
    private record ParcelaMin(Guid Id, Guid ContratoId, decimal Valor);
    private record TokenMin(string AccessToken);
    private record CobrancaMin(string ChargeId, string Status);
}
