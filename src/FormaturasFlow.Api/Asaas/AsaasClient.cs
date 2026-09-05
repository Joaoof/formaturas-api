using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Asaas;

public class AsaasClient(HttpClient http, IOptions<AsaasOptions> opt, ILogger<AsaasClient> log)
{
    private readonly AsaasOptions _opt = opt.Value;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public record CriarClienteRequest(
        string Name,
        string CpfCnpj,
        string? Email = null,
        string? Phone = null,
        string? MobilePhone = null);

    public record ClienteCriado(
        string Id,
        string Name,
        string CpfCnpj);

    public enum BillingType { BOLETO, PIX, CREDIT_CARD, UNDEFINED }

    public record CriarCobrancaRequest(
        string Customer,
        BillingType BillingType,
        decimal Value,
        DateOnly DueDate,
        string Description,
        string? ExternalReference = null,
        int? InstallmentCount = null,
        decimal? InstallmentValue = null);

    public record CobrancaCriada(
        string Id,
        string Status,
        string BillingType,
        decimal Value,
        string? InvoiceUrl,
        string? BankSlipUrl,
        string? IdentificationField,
        string? NossoNumero);

    public record PixQrCode(
        string EncodedImage,
        string Payload,
        DateTimeOffset? ExpirationDate);

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{_opt.BaseUrl}{path}");
        req.Headers.Add("access_token", _opt.ApiKey);
        req.Headers.Add("User-Agent", "FormaturasFlow/1.0");
        return req;
    }

    public async Task<ClienteCriado> CriarClienteAsync(CriarClienteRequest r, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, "/customers");
        req.Content = JsonContent.Create(new
        {
            name = r.Name,
            cpfCnpj = OnlyDigits(r.CpfCnpj),
            email = r.Email,
            phone = r.Phone,
            mobilePhone = r.MobilePhone
        }, options: JsonOpts);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Asaas /customers falhou: {Status} {Body}", resp.StatusCode, body);
            throw new AsaasException($"Falha ao criar cliente: {resp.StatusCode}", body);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new ClienteCriado(
            Id: root.GetProperty("id").GetString()!,
            Name: root.GetProperty("name").GetString() ?? r.Name,
            CpfCnpj: root.GetProperty("cpfCnpj").GetString() ?? r.CpfCnpj);
    }

    public async Task<CobrancaCriada> CriarCobrancaAsync(CriarCobrancaRequest r, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, r.InstallmentCount is > 1 ? "/installments" : "/payments");
        req.Content = JsonContent.Create(new
        {
            customer = r.Customer,
            billingType = r.BillingType.ToString(),
            value = r.Value,
            dueDate = r.DueDate.ToString("yyyy-MM-dd"),
            description = r.Description,
            externalReference = r.ExternalReference,
            installmentCount = r.InstallmentCount,
            installmentValue = r.InstallmentValue
        }, options: JsonOpts);

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Asaas /payments falhou: {Status} {Body}", resp.StatusCode, body);
            throw new AsaasException($"Falha ao criar cobranca: {resp.StatusCode}", body);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new CobrancaCriada(
            Id: root.GetProperty("id").GetString()!,
            Status: (root.TryGetProperty("status", out var st) ? st.GetString() : null) ?? "PENDING",
            BillingType: (root.TryGetProperty("billingType", out var bt) ? bt.GetString() : null) ?? r.BillingType.ToString(),
            Value: root.TryGetProperty("value", out var vl) ? vl.GetDecimal() : r.Value,
            InvoiceUrl: root.TryGetProperty("invoiceUrl", out var inv) ? inv.GetString() : null,
            BankSlipUrl: root.TryGetProperty("bankSlipUrl", out var bs) ? bs.GetString() : null,
            IdentificationField: root.TryGetProperty("identificationField", out var id) ? id.GetString() : null,
            NossoNumero: root.TryGetProperty("nossoNumero", out var nn) ? nn.GetString() : null);
    }

    public async Task<PixQrCode> BuscarPixQrCodeAsync(string paymentId, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, $"/payments/{paymentId}/pixQrCode");
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Asaas /pixQrCode falhou: {Status} {Body}", resp.StatusCode, body);
            throw new AsaasException($"Falha ao buscar QR PIX: {resp.StatusCode}", body);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        DateTimeOffset? exp = null;
        if (root.TryGetProperty("expirationDate", out var expEl) && expEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(expEl.GetString(), out var parsed))
            exp = parsed;

        return new PixQrCode(
            EncodedImage: root.GetProperty("encodedImage").GetString() ?? string.Empty,
            Payload: root.GetProperty("payload").GetString() ?? string.Empty,
            ExpirationDate: exp);
    }

    private static string OnlyDigits(string s) => new(s.Where(char.IsDigit).ToArray());
}

public class AsaasException(string message, string? responseBody = null) : Exception(message)
{
    public string? ResponseBody { get; } = responseBody;
}
