using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Efi;

public class EfiClient(HttpClient http, IOptions<EfiOptions> opt, ILogger<EfiClient> log)
{
    private readonly EfiOptions _opt = opt.Value;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpira = DateTimeOffset.MinValue;

    public record CriarBoletoRequest(
        decimal Valor,
        DateOnly Vencimento,
        string PayerNome,
        string PayerCpf,
        string Descricao);

    public record BoletoCriado(
        string ChargeId,
        string LinhaDigitavel,
        string CodigoBarras,
        string BoletoUrl,
        string Status);

    public record CriarPixRequest(
        string TxId,
        decimal Valor,
        string PayerCpf,
        string PayerNome,
        string Descricao,
        int ExpiraSegundos = 3600);

    public record PixCriado(
        string TxId,
        string CopiaCola,
        string QrCodeUrl,
        string Status);

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpira.AddMinutes(-1))
            return _cachedToken;

        var basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"{_opt.CobrancasBaseUrl}/v1/authorize");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = JsonContent.Create(new { grant_type = "client_credentials" });

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        _cachedToken = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Resposta sem access_token.");
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpira = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        return _cachedToken;
    }

    public async Task<BoletoCriado> CriarBoletoAsync(CriarBoletoRequest req, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            items = new[] { new { name = req.Descricao, value = (int)(req.Valor * 100), amount = 1 } },
            payment = new
            {
                banking_billet = new
                {
                    expire_at = req.Vencimento.ToString("yyyy-MM-dd"),
                    customer = new
                    {
                        name = req.PayerNome,
                        cpf = new string(req.PayerCpf.Where(char.IsDigit).ToArray())
                    }
                }
            }
        };

        using var resp = await http.PostAsJsonAsync($"{_opt.CobrancasBaseUrl}/v1/charge/one-step", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Efí boleto falhou: {Status} {Body}", resp.StatusCode, body);
            throw new EfiException($"Falha ao criar boleto: {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        return new BoletoCriado(
            ChargeId: data.GetProperty("charge_id").GetInt64().ToString(),
            LinhaDigitavel: data.GetProperty("barcode").GetString() ?? string.Empty,
            CodigoBarras: data.GetProperty("barcode").GetString() ?? string.Empty,
            BoletoUrl: data.GetProperty("link").GetString() ?? string.Empty,
            Status: data.GetProperty("status").GetString() ?? "waiting");
    }

    public async Task<PixCriado> CriarPixAsync(CriarPixRequest req, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            calendario = new { expiracao = req.ExpiraSegundos },
            devedor = new
            {
                cpf = new string(req.PayerCpf.Where(char.IsDigit).ToArray()),
                nome = req.PayerNome
            },
            valor = new { original = req.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) },
            chave = _opt.PixKey,
            solicitacaoPagador = req.Descricao
        };

        using var resp = await http.PutAsJsonAsync($"{_opt.PixBaseUrl}/v2/cob/{req.TxId}", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Efí PIX falhou: {Status} {Body}", resp.StatusCode, body);
            throw new EfiException($"Falha ao criar PIX: {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var loc = doc.RootElement.GetProperty("loc");
        return new PixCriado(
            TxId: req.TxId,
            CopiaCola: doc.RootElement.GetProperty("pixCopiaECola").GetString() ?? string.Empty,
            QrCodeUrl: loc.GetProperty("location").GetString() ?? string.Empty,
            Status: doc.RootElement.GetProperty("status").GetString() ?? "ATIVA");
    }
}

public class EfiException(string message) : Exception(message);
