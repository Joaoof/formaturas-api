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

/*  Caminho completo do recebimento: a Cora avisa → a API reconsulta →
    a parcela é baixada no banco.

    A Cora é substituída por um dublê porque não há como PAGAR um boleto no
    ambiente de stage; o que precisa ser provado aqui não é o HTTP com o
    provedor (isso o CoraLiveTests já cobre contra a Cora real), e sim que
    uma fatura paga vira parcela quitada de verdade no Postgres.  */
[Collection("api")]
public class CoraWebhookBaixaTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string ChargeId = "inv_TesteBaixaWebhook";
    private const string Rota = "/api/v1/pagamentos/webhooks/cora";

    /*  Dublê da Cora: devolve a fatura no status que o teste pedir.  */
    private sealed class CoraFake(Func<string, CobrancaCriada> resposta) : IConsultaCobranca
    {
        public PaymentProvider Provider => PaymentProvider.Cora;

        public Task<CobrancaCriada> ConsultarCobrancaAsync(string chargeId, CancellationToken ct = default) =>
            Task.FromResult(resposta(chargeId));
    }

    private HttpClient ComCoraRespondendo(Func<string, CobrancaCriada> resposta) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IConsultaCobranca>();
            s.AddTransient<IConsultaCobranca>(_ => new CoraFake(resposta));
        })).CreateClient();

    private static CobrancaCriada Fatura(string status, decimal? valorPago) => new(
        PaymentProvider.Cora, ChargeId, MetodoPagamento.Boleto, status, ValorPago: valorPago);

    private static HttpRequestMessage Aviso(string? segredo = ApiFactory.CoraWebhookSecret)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, Rota)
        {
            Content = JsonContent.Create(new { resource = new { id = ChargeId }, @event = "invoice.paid" })
        };

        if (segredo is not null)
            msg.Headers.Add("X-Webhook-Secret", segredo);

        return msg;
    }

    /*  Cria contrato+parcela pelos endpoints reais e vincula a parcela à
        cobrança, que é o estado em que o webhook a encontra em produção.  */
    private async Task<(Guid ParcelaId, decimal Valor)> ArranjarParcelaVinculadaAsync()
    {
        var http = factory.CreateClient();
        var tok = await http.RegisterAsync("admin@cora.com", "SenhaForte1!", "Admin");
        http.WithToken(tok.AccessToken);

        await http.PostAsJsonAsync("/api/v1/turmas", new { nome = "Formatura 2026" });
        var turma = (await http.GetFromJsonAsync<TurmaMin[]>("/api/v1/turmas"))![0];

        var alunoResp = await http.PostAsJsonAsync("/api/v1/alunos",
            new { turmaId = turma.Id, nomeCompleto = "Formando Teste" });
        var aluno = await alunoResp.Content.ReadFromJsonAsync<AlunoMin>();

        await http.PostAsJsonAsync("/api/v1/contratos", new
        {
            alunoId = aluno!.Id,
            valorTotal = 1000m,
            valorEntrada = 0m,
            numParcelas = 1,
            dataContrato = "2026-01-01",
            primeiroVencimento = "2026-02-05"
        });

        var parcela = (await http.GetFromJsonAsync<ParcelaMin[]>("/api/v1/parcelas"))![0];

        await using var escopo = factory.Services.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<FormaturasFlow.Api.Data.AppDbContext>();
        var alvo = await db.Parcelas.FindAsync(parcela.Id);
        alvo!.PspProvider = "cora";
        alvo.PspChargeId = ChargeId;
        alvo.PspStatus = "OPEN";
        await db.SaveChangesAsync();

        return (parcela.Id, parcela.Valor);
    }

    private async Task<FormaturasFlow.Api.Domain.Parcela> RecarregarAsync(Guid id)
    {
        await using var escopo = factory.Services.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<FormaturasFlow.Api.Data.AppDbContext>();
        return (await db.Parcelas.FindAsync(id))!;
    }

    [Fact]
    public async Task Fatura_Paga_Baixa_A_Parcela_No_Banco()
    {
        var (parcelaId, valor) = await ArranjarParcelaVinculadaAsync();
        var http = ComCoraRespondendo(_ => Fatura("PAID", valor));

        var resp = await http.SendAsync(Aviso());

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var parcela = await RecarregarAsync(parcelaId);
        parcela.Status.Should().Be(FormaturasFlow.Api.Domain.StatusParcela.Pago);
        parcela.ValorPago.Should().Be(valor);
        parcela.DataPagamento.Should().NotBeNull();
        parcela.PspStatus.Should().Be("PAID");
    }

    /*  A garantia contra POST forjado: o estado vem da reconsulta, não do
        corpo.  O aviso diz "invoice.paid", mas a Cora responde OPEN.  */
    [Fact]
    public async Task Aviso_De_Pago_Com_Fatura_Aberta_Nao_Baixa_Nada()
    {
        var (parcelaId, _) = await ArranjarParcelaVinculadaAsync();
        var http = ComCoraRespondendo(_ => Fatura("OPEN", null));

        var resp = await http.SendAsync(Aviso());

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var parcela = await RecarregarAsync(parcelaId);
        parcela.Status.Should().Be(FormaturasFlow.Api.Domain.StatusParcela.Pendente);
        parcela.DataPagamento.Should().BeNull();
    }

    [Fact]
    public async Task Pagamento_Parcial_Registra_Valor_E_Mantem_Pendente()
    {
        var (parcelaId, _) = await ArranjarParcelaVinculadaAsync();
        var http = ComCoraRespondendo(_ => Fatura("PAID_PARTIALLY", 300m));

        await http.SendAsync(Aviso());

        var parcela = await RecarregarAsync(parcelaId);
        parcela.Status.Should().Be(FormaturasFlow.Api.Domain.StatusParcela.Pendente);
        parcela.ValorPago.Should().Be(300m);
    }

    /*  Parcial seguido de quitação precisa terminar com o valor CHEIO: era
        aqui que a primeira versão deixava R$ 300 gravados numa parcela de
        R$ 1.000 já quitada.  */
    [Fact]
    public async Task Parcial_Seguido_De_Quitacao_Termina_Com_Valor_Cheio()
    {
        var (parcelaId, valor) = await ArranjarParcelaVinculadaAsync();

        await ComCoraRespondendo(_ => Fatura("PAID_PARTIALLY", 300m)).SendAsync(Aviso());
        await ComCoraRespondendo(_ => Fatura("PAID", valor)).SendAsync(Aviso());

        var parcela = await RecarregarAsync(parcelaId);
        parcela.Status.Should().Be(FormaturasFlow.Api.Domain.StatusParcela.Pago);
        parcela.ValorPago.Should().Be(valor);
    }

    [Fact]
    public async Task Aviso_Repetido_Nao_Baixa_Duas_Vezes()
    {
        var (parcelaId, valor) = await ArranjarParcelaVinculadaAsync();
        var http = ComCoraRespondendo(_ => Fatura("PAID", valor));

        await http.SendAsync(Aviso());
        var primeira = (await RecarregarAsync(parcelaId)).AtualizadaEm;

        var segunda = await http.SendAsync(Aviso());

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        var parcela = await RecarregarAsync(parcelaId);
        parcela.ValorPago.Should().Be(valor);
        parcela.AtualizadaEm.Should().Be(primeira);
    }

    [Fact]
    public async Task Sem_Segredo_Recusa_Sem_Chamar_A_Cora()
    {
        await ArranjarParcelaVinculadaAsync();

        var chamou = false;
        var http = ComCoraRespondendo(_ =>
        {
            chamou = true;
            return Fatura("PAID", 1000m);
        });

        var resp = await http.SendAsync(Aviso(segredo: null));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        chamou.Should().BeFalse();
    }

    [Fact]
    public async Task Segredo_Errado_Recusa()
    {
        var (parcelaId, _) = await ArranjarParcelaVinculadaAsync();
        var http = ComCoraRespondendo(_ => Fatura("PAID", 1000m));

        var resp = await http.SendAsync(Aviso(segredo: "segredo-errado"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RecarregarAsync(parcelaId)).Status
            .Should().Be(FormaturasFlow.Api.Domain.StatusParcela.Pendente);
    }

    /*  Caminho alternativo para quando o painel da Cora só aceitar URL, sem
        cabeçalho customizado.  */
    [Fact]
    public async Task Segredo_Na_Query_Tambem_Autoriza()
    {
        var (parcelaId, valor) = await ArranjarParcelaVinculadaAsync();
        var http = ComCoraRespondendo(_ => Fatura("PAID", valor));

        var resp = await http.PostAsJsonAsync(
            $"{Rota}?secret={ApiFactory.CoraWebhookSecret}",
            new { resource = new { id = ChargeId } });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RecarregarAsync(parcelaId)).Status
            .Should().Be(FormaturasFlow.Api.Domain.StatusParcela.Pago);
    }

    [Fact]
    public async Task Segredo_Errado_Na_Query_Recusa()
    {
        var (parcelaId, _) = await ArranjarParcelaVinculadaAsync();
        var http = ComCoraRespondendo(_ => Fatura("PAID", 1000m));

        var resp = await http.PostAsJsonAsync($"{Rota}?secret=errado",
            new { resource = new { id = ChargeId } });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RecarregarAsync(parcelaId)).Status
            .Should().Be(FormaturasFlow.Api.Domain.StatusParcela.Pendente);
    }

    /*  Cobrança sem parcela vinculada (teste, avulsa): o evento é guardado
        para auditoria e o endpoint não quebra.  */
    [Fact]
    public async Task Fatura_Sem_Parcela_Vinculada_Responde_Ok()
    {
        var http = ComCoraRespondendo(_ => Fatura("PAID", 500m));

        var resp = await http.SendAsync(Aviso());

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private record TurmaMin(Guid Id, string Nome);
    private record AlunoMin(Guid Id, Guid TurmaId, string NomeCompleto);
    private record ParcelaMin(Guid Id, int Numero, decimal Valor);
}
