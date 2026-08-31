using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

/*  Adapter da Cora (PSP das Formaturas).

    A Cora emite boleto e Pix; cartão de crédito ela simplesmente não
    processa — por isso `Suporta` devolve false para cartão e a factory
    barra o pedido antes de qualquer chamada HTTP.  */
public sealed class CoraPaymentGateway(
    HttpClient http,
    IOptions<CoraOptions> opt,
    ILogger<CoraPaymentGateway> log) : IPaymentGateway
{
    private readonly CoraOptions _opt = opt.Value;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpira = DateTimeOffset.MinValue;

    public PaymentProvider Provider => PaymentProvider.Cora;

    public bool Suporta(MetodoPagamento metodo) => metodo is
        MetodoPagamento.Boleto or MetodoPagamento.Pix;

    public async Task<CobrancaCriada> CriarCobrancaAsync(CobrancaRequest req, CancellationToken ct = default)
    {
        if (!Suporta(req.Metodo))
            throw new PaymentGatewayException(Provider, $"A Cora não processa {req.Metodo}.");

        var token = await GetAccessTokenAsync(ct);

        var payload = new
        {
            code = req.ReferenciaExterna,
            customer = new
            {
                name  = req.Pagador.Nome,
                email = req.Pagador.Email,
                document = new
                {
                    identity = Digitos(req.Pagador.Documento),
                    type     = Digitos(req.Pagador.Documento).Length > 11 ? "CNPJ" : "CPF"
                }
            },
            services = new[]
            {
                new
                {
                    name        = req.Descricao,
                    description = req.Descricao,

                    /*  A Cora trabalha em centavos.  */
                    amount = (int)Math.Round(req.Valor * 100m, MidpointRounding.AwayFromZero)
                }
            },
            payment_terms = new { due_date = req.Vencimento.ToString("yyyy-MM-dd") },
            payment_forms = new[] { PaymentForm(req.Metodo) }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_opt.BaseUrl}/v2/invoices");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Add("Idempotency-Key", req.ReferenciaExterna);
        msg.Content = JsonContent.Create(payload);

        using var resp = await http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Cora cobrança falhou: {Status} {Body}", resp.StatusCode, body);
            throw new PaymentGatewayException(Provider, $"Falha ao criar cobrança na Cora: {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var raiz = doc.RootElement;
        var opcoes = Filho(raiz, "payment_options");
        var boleto = Filho(opcoes, "bank_slip");
        var pix    = Filho(opcoes, "pix");

        return new CobrancaCriada(
            Provider: Provider,
            ChargeId: Texto(raiz, "id") ?? string.Empty,
            Metodo: req.Metodo,
            Status: Texto(raiz, "status") ?? "OPEN",
            LinkPagamento: Texto(boleto, "url") ?? Texto(pix, "url"),
            BoletoUrl: Texto(boleto, "url"),
            BoletoLinhaDigitavel: Texto(boleto, "digitable"),
            BoletoCodigoBarras: Texto(boleto, "barcode"),
            PixCopiaCola: Texto(pix, "emv"),
            PixQrCodeUrl: Texto(pix, "qr_code_url") ?? Texto(pix, "url"));
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpira.AddMinutes(-1))
            return _cachedToken;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opt.BaseUrl}/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"]  = _opt.ClientId
        });

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Cora token falhou: {Status} {Body}", resp.StatusCode, body);
            throw new PaymentGatewayException(Provider, $"Falha ao autenticar na Cora: {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        _cachedToken = Texto(doc.RootElement, "access_token")
            ?? throw new PaymentGatewayException(Provider, "Resposta sem access_token.");

        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var segundos)
            ? segundos
            : 300;
        _tokenExpira = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        return _cachedToken;
    }

    private static string PaymentForm(MetodoPagamento metodo) => metodo switch
    {
        MetodoPagamento.Boleto => "BANK_SLIP",
        MetodoPagamento.Pix    => "PIX",
        _                      => throw new PaymentGatewayException(PaymentProvider.Cora, $"Método {metodo} não mapeado.")
    };

    private static string Digitos(string valor) => new(valor.Where(char.IsDigit).ToArray());

    private static JsonElement Filho(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object && elemento.TryGetProperty(propriedade, out var v)
            ? v
            : default;

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
            && elemento.TryGetProperty(propriedade, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
