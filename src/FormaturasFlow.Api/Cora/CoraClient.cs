using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Cora;

public class CoraClient(HttpClient http, IOptions<CoraOptions> opt, ILogger<CoraClient> log)
{
    private readonly CoraOptions _opt = opt.Value;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpira = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public enum InvoiceKind { PIX, BOLETO }

    public record CriarInvoiceRequest(
        InvoiceKind Kind,
        decimal Value,
        DateOnly DueDate,
        string CustomerName,
        string CustomerDocument,
        string? CustomerEmail,
        string Description);

    public record InvoiceCriada(
        string Id,
        string Status,
        string Kind,
        decimal Value,
        string? PixEmv,
        string? PixQrCodeBase64,
        string? BoletoUrl,
        string? BoletoLinhaDigitavel,
        string? BoletoCodigoBarras);

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpira.AddMinutes(-1))
            return _cachedToken;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.BaseUrl}/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _opt.ClientId
        });

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Cora /token falhou: {Status} {Body}", resp.StatusCode, body);
            throw new CoraException($"Falha na autenticacao: {resp.StatusCode}", body);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        _cachedToken = root.GetProperty("access_token").GetString()
            ?? throw new CoraException("Resposta sem access_token", body);
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        _tokenExpira = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        return _cachedToken;
    }

    public async Task<InvoiceCriada> CriarInvoiceAsync(CriarInvoiceRequest r, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.BaseUrl}/v2/invoices/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Content = JsonContent.Create(new
        {
            code = idempotencyKey,
            customer = new
            {
                name = r.CustomerName,
                email = r.CustomerEmail,
                document = new
                {
                    identity = OnlyDigits(r.CustomerDocument),
                    rule = "CPF"
                }
            },
            services = new[]
            {
                new
                {
                    name = r.Description,
                    description = r.Description,
                    amount = (long)(r.Value * 100)
                }
            },
            paymentTerms = new
            {
                dueDate = r.DueDate.ToString("yyyy-MM-dd")
            },
            paymentForms = new[] { r.Kind == InvoiceKind.PIX ? "PIX" : "BANK_SLIP" }
        }, options: JsonOpts);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Cora /invoices falhou: {Status} {Body}", resp.StatusCode, body);
            throw new CoraException($"Falha ao criar invoice: {resp.StatusCode}", body);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string? pixEmv = null, pixQr = null, boletoUrl = null, linha = null, cb = null;

        if (root.TryGetProperty("pix", out var pix))
        {
            pixEmv = pix.TryGetProperty("emv", out var em) ? em.GetString() : null;
            pixQr = pix.TryGetProperty("qrCode", out var qr) ? qr.GetString() : null;
        }
        if (root.TryGetProperty("boleto", out var bol))
        {
            boletoUrl = bol.TryGetProperty("url", out var u) ? u.GetString() : null;
            linha = bol.TryGetProperty("digitableLine", out var dl) ? dl.GetString() : null;
            cb = bol.TryGetProperty("barCode", out var bc) ? bc.GetString() : null;
        }

        return new InvoiceCriada(
            Id: root.GetProperty("id").GetString()!,
            Status: (root.TryGetProperty("status", out var st) ? st.GetString() : null) ?? "OPEN",
            Kind: r.Kind.ToString(),
            Value: r.Value,
            PixEmv: pixEmv,
            PixQrCodeBase64: pixQr,
            BoletoUrl: boletoUrl,
            BoletoLinhaDigitavel: linha,
            BoletoCodigoBarras: cb);
    }

    private static string OnlyDigits(string s) => new(s.Where(char.IsDigit).ToArray());
}

public class CoraException(string message, string? responseBody = null) : Exception(message)
{
    public string? ResponseBody { get; } = responseBody;
}
