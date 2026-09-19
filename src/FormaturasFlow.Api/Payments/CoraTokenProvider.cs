using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

/*  Cache do access_token da Cora, com tempo de vida de PROCESSO.

    Precisa ser singleton: o gateway é typed client (transiente), então um
    cache guardado nele nasceria vazio a cada requisição e toda emissão
    pagaria um /token antes — caminho curto para esbarrar em rate limit do
    provedor justamente no pico de emissão.

    O HttpClient vem por factory para não prender o handler mTLS num
    singleton (socket obsoleto depois de mudança de DNS).  */
public sealed class CoraTokenProvider(
    IHttpClientFactory httpFactory,
    IOptions<CoraOptions> opt,
    CoraCredentials credenciais,
    ILogger<CoraTokenProvider> log)
{
    public const string HttpClientName = "cora-token";

    private readonly CoraOptions _opt = opt.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiraEm = DateTimeOffset.MinValue;

    public async Task<string> ObterAsync(CancellationToken ct)
    {
        if (Valido)
            return _token!;

        await _lock.WaitAsync(ct);
        try
        {
            /*  Outra requisição pode ter renovado enquanto esperávamos.  */
            if (Valido)
                return _token!;

            if (!credenciais.Configurada)
                throw new PaymentGatewayException(PaymentProvider.Cora,
                    "Credencial da Cora ausente: configure Cora:CertificatePemPath e Cora:PrivateKeyPemPath (ou Cora:CertificateBase64).");

            var http = httpFactory.CreateClient(HttpClientName);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.BaseUrl}/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"]  = credenciais.ClientId
                })
            };

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogError("Cora token falhou: {Status} {Body}", resp.StatusCode, CoraPaymentGateway.Resumir(body));
                throw new PaymentGatewayException(PaymentProvider.Cora, $"Falha ao autenticar na Cora: {resp.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            var raiz = doc.RootElement;

            _token = raiz.TryGetProperty("access_token", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : throw new PaymentGatewayException(PaymentProvider.Cora, "Resposta sem access_token.");

            var segundos = raiz.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var s) ? s : 300;

            /*  Margem de 1 minuto: token que expira em trânsito vira 401 numa
                emissão que já debitou o tempo do usuário.  */
            _expiraEm = DateTimeOffset.UtcNow.AddSeconds(segundos).AddMinutes(-1);

            return _token!;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool Valido => _token is not null && DateTimeOffset.UtcNow < _expiraEm;
}
