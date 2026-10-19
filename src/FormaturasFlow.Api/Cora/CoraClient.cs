using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using FormaturasFlow.Api.Payments;

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

    /*  Envio do payload: mantem os nomes em snake_case (SEM camelCase, que a
        Cora rejeita em payment_terms) e omite campos nulos — assim o bloco
        `notification` so vai quando ha e-mail do cliente.  */
    private static readonly JsonSerializerOptions JsonSend = new()
    {
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
        string Description,
        string ExternalReference);

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
        /*  A Cora RECUSA (400) qualquer Idempotency-Key fora do formato UUID.
            ToString("N") gerava 32 hex sem hifens → invalido.  UUIDv5
            deterministico a partir da referencia externa: mesma parcela gera
            o mesmo UUID, entao retry nao cria fatura duplicada no Cora.  */
        var idempotencyKey = CoraPaymentGateway.ChaveIdempotencia(r.ExternalReference).ToString();

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.BaseUrl}/v2/invoices/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        /*  Payload alinhado ao /v2/invoices atual da Cora: nomes em
            snake_case (payment_terms, due_date, payment_forms), `type` em vez
            de `rule`, `code` = referencia externa e SEM politica camelCase
            (que renomeava payment_terms -> paymentTerms e a Cora rejeitava
            com "paymentTerms must not be null").  */
        var documento = OnlyDigits(r.CustomerDocument);

        /*  Notificacao por e-mail: faz a Cora enviar o boleto/cobranca para o
            e-mail do cliente (lembrete antes do vencimento, no dia, e aviso de
            pagamento). So incluimos quando ha e-mail — JsonSend omite o nulo.  */
        object? notification = string.IsNullOrWhiteSpace(r.CustomerEmail) ? null : new
        {
            name     = r.CustomerName,
            channels = new[]
            {
                new
                {
                    channel = "EMAIL",
                    contact = r.CustomerEmail,
                    rules   = new[] { "NOTIFY_TWO_DAYS_BEFORE_DUE_DATE", "NOTIFY_ON_DUE_DATE", "NOTIFY_WHEN_PAID" }
                }
            }
        };

        req.Content = JsonContent.Create(new
        {
            code = r.ExternalReference,
            customer = new
            {
                name = r.CustomerName,
                email = r.CustomerEmail,
                document = new
                {
                    identity = documento,
                    type     = documento.Length > 11 ? "CNPJ" : "CPF"
                }
            },
            services = new[]
            {
                new
                {
                    name        = r.Description,
                    description = r.Description,
                    amount      = (long)Math.Round(r.Value * 100m, MidpointRounding.AwayFromZero)
                }
            },
            payment_terms = new { due_date = r.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            payment_forms = new[] { r.Kind == InvoiceKind.PIX ? "PIX" : "BANK_SLIP" },
            notification
        }, options: JsonSend);

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
