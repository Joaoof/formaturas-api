using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

/*  Cadastro dos endpoints de notificação na Cora.

    A Cora entrega o aviso de pagamento num endereço que PRECISA estar
    registrado por API — não existe tela para isso na Integração Direta.
    Enquanto esse cadastro não acontece, o boleto é pago e o sistema nunca
    sabe: a parcela fica pendente com o dinheiro na conta.

    Verificado contra o ambiente de stage: POST devolve 200 com o endpoint
    criado, GET lista, DELETE devolve 204.  */
public sealed class CoraEndpointsClient(
    HttpClient http,
    IOptions<CoraOptions> opt,
    CoraTokenProvider tokens,
    ILogger<CoraEndpointsClient> log)
{
    private readonly CoraOptions _opt = opt.Value;

    /*  Recursos e gatilhos aceitos pela Cora.  `*` vale como "todos".  */
    public static readonly string[] Recursos =
        ["invoice", "transfer", "payment", "register", "service_receipt", "*"];

    public static readonly string[] GatilhosDeFatura =
        ["drafted", "created", "paid", "canceled", "overdue", "*"];

    public record Endpoint(
        string  Id,
        string  Url,
        string  Resource,
        string  Trigger,
        bool    Active,
        bool    IncludeResource,
        int?    ConnectionTimeout,
        int?    ReadTimeout);

    public async Task<IReadOnlyList<Endpoint>> ListarAsync(CancellationToken ct = default)
    {
        using var msg = Requisicao(HttpMethod.Get, "/endpoints");
        msg.Headers.Authorization = await AutorizacaoAsync(ct);

        using var resp = await http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        Garantir(resp, body, "listar endpoints");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        return doc.RootElement.EnumerateArray().Select(Mapear).ToArray();
    }

    /*  `url` precisa carregar o segredo na query.

        A Cora não aceita cabeçalho customizado no aviso — o cadastro só tem
        url, recurso e gatilho.  Logo, a única forma de o nosso webhook
        distinguir a Cora de um POST qualquer é o `?secret=` embutido aqui.  */
    public async Task<Endpoint> CriarAsync(
        string url,
        string recurso = "invoice",
        string gatilho = "*",
        bool incluirRecurso = false,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new PaymentGatewayException(PaymentProvider.Cora,
                "A URL do webhook precisa ser absoluta e https.");

        if (!Recursos.Contains(recurso, StringComparer.OrdinalIgnoreCase))
            throw new PaymentGatewayException(PaymentProvider.Cora,
                $"Recurso '{recurso}' não é aceito pela Cora. Use: {string.Join(", ", Recursos)}.");

        using var msg = Requisicao(HttpMethod.Post, "/endpoints");
        msg.Headers.Authorization = await AutorizacaoAsync(ct);

        /*  A Cora recusa Idempotency-Key fora do formato UUID, igual na
            emissão de cobrança.  Derivado da combinação para reenviar o
            mesmo cadastro não criar duplicata.  */
        msg.Headers.Add("Idempotency-Key",
            CoraPaymentGateway.ChaveIdempotencia($"endpoint|{url}|{recurso}|{gatilho}").ToString());

        msg.Content = JsonContent.Create(new
        {
            url,
            resource = recurso,
            trigger = gatilho,
            includeResource = incluirRecurso
        });

        using var resp = await http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        Garantir(resp, body, "cadastrar endpoint");

        using var doc = JsonDocument.Parse(body);
        var criado = Mapear(doc.RootElement);

        /*  O log não leva a URL inteira porque ela carrega o segredo.  */
        log.LogInformation("Endpoint da Cora cadastrado: {Id} para {Recurso}.{Gatilho}.",
            criado.Id, criado.Resource, criado.Trigger);

        return criado;
    }

    public async Task RemoverAsync(string id, CancellationToken ct = default)
    {
        using var msg = Requisicao(HttpMethod.Delete, $"/endpoints/{Uri.EscapeDataString(id)}");
        msg.Headers.Authorization = await AutorizacaoAsync(ct);

        using var resp = await http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        Garantir(resp, body, $"remover endpoint {id}");

        log.LogInformation("Endpoint da Cora removido: {Id}.", id);
    }

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho) =>
        new(metodo, $"{_opt.BaseUrl}{caminho}");

    private async Task<AuthenticationHeaderValue> AutorizacaoAsync(CancellationToken ct) =>
        new("Bearer", await tokens.ObterAsync(ct));

    private void Garantir(HttpResponseMessage resp, string body, string operacao)
    {
        if (resp.IsSuccessStatusCode)
            return;

        log.LogError("Cora falhou ao {Operacao}: {Status} {Body}",
            operacao, resp.StatusCode, CoraPaymentGateway.Resumir(body));

        throw new PaymentGatewayException(PaymentProvider.Cora,
            $"Falha ao {operacao} na Cora: {resp.StatusCode}");
    }

    internal static Endpoint Mapear(JsonElement e) => new(
        Id: Texto(e, "id") ?? string.Empty,
        Url: Texto(e, "url") ?? string.Empty,
        Resource: Texto(e, "resource") ?? string.Empty,
        Trigger: Texto(e, "trigger") ?? string.Empty,
        Active: Booleano(e, "active") ?? true,
        IncludeResource: Booleano(e, "includeResource") ?? false,
        ConnectionTimeout: Inteiro(e, "connectionTimeout"),
        ReadTimeout: Inteiro(e, "readTimeout"));

    private static string? Texto(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool? Booleano(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
            && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static int? Inteiro(JsonElement e, string p) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : null;
}
