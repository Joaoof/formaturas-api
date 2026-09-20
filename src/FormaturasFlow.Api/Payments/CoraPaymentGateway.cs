using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
    CoraTokenProvider tokens,
    ILogger<CoraPaymentGateway> log) : IPaymentGateway, IConsultaCobranca
{
    private readonly CoraOptions _opt = opt.Value;

    /*  A Cora trabalha em centavos e o campo é int32.  */
    private const decimal ValorMaximo = int.MaxValue / 100m;

    public PaymentProvider Provider => PaymentProvider.Cora;

    public bool Suporta(MetodoPagamento metodo) => metodo is
        MetodoPagamento.Boleto or MetodoPagamento.Pix;

    public async Task<CobrancaCriada> CriarCobrancaAsync(CobrancaRequest req, CancellationToken ct = default)
    {
        if (!Suporta(req.Metodo))
            throw new PaymentGatewayException(Provider, $"A Cora não processa {req.Metodo}.");

        var centavos = Centavos(req.Valor);
        var token = await tokens.ObterAsync(ct);
        var documento = Digitos(req.Pagador.Documento);

        var payload = new
        {
            code = req.ReferenciaExterna,
            customer = new
            {
                name  = req.Pagador.Nome,
                email = req.Pagador.Email,
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
                    name        = req.Descricao,
                    description = req.Descricao,
                    amount      = centavos
                }
            },

            /*  InvariantCulture explícito: em host com cultura de calendário
                não gregoriano, o formato "yyyy" renderiza outro ano e a
                cobrança nasce com vencimento errado.  */
            payment_terms = new { due_date = req.Vencimento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            payment_forms = new[] { PaymentForm(req.Metodo) }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_opt.BaseUrl}/v2/invoices");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        /*  A Cora RECUSA (400) qualquer Idempotency-Key que não seja UUID, e
            a referência externa daqui é "parcela-123".  Derivar o UUID da
            referência — em vez de sortear — preserva o efeito que importa:
            reenviar a mesma parcela devolve a mesma fatura, sem cobrar duas
            vezes o formando.  */
        msg.Headers.Add("Idempotency-Key", ChaveIdempotencia(req.ReferenciaExterna).ToString());
        msg.Content = JsonContent.Create(payload);

        using var resp = await http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Cora cobrança falhou: {Status} {Body}", resp.StatusCode, Resumir(body));
            throw new PaymentGatewayException(Provider, $"Falha ao criar cobrança na Cora: {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        return Mapear(doc.RootElement, req.Metodo);
    }

    public async Task<CobrancaCriada> ConsultarCobrancaAsync(string chargeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chargeId))
            throw new PaymentGatewayException(Provider, "Id da cobrança não informado.");

        var token = await tokens.ObterAsync(ct);

        using var msg = new HttpRequestMessage(HttpMethod.Get, $"{_opt.BaseUrl}/v2/invoices/{Uri.EscapeDataString(chargeId)}");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Cora consulta falhou: {Status} {Body}", resp.StatusCode, Resumir(body));
            throw new PaymentGatewayException(Provider, $"Falha ao consultar cobrança na Cora: {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        return Mapear(doc.RootElement, metodo: null);
    }

    /*  Centavos como int32, com o limite checado ANTES da conversão.  Sem
        isso, um contrato lançado com valor absurdo derruba a emissão com
        OverflowException → 500, em vez de um erro de domínio legível.  */
    internal static int Centavos(decimal valor)
    {
        if (valor <= 0m)
            throw new PaymentGatewayException(PaymentProvider.Cora,
                $"Valor da cobrança precisa ser maior que zero (recebido: {valor.ToString(CultureInfo.InvariantCulture)}).");

        if (valor > ValorMaximo)
            throw new PaymentGatewayException(PaymentProvider.Cora,
                $"Valor da cobrança excede o limite da Cora ({ValorMaximo.ToString("N2", CultureInfo.InvariantCulture)}).");

        return (int)Math.Round(valor * 100m, MidpointRounding.AwayFromZero);
    }

    /*  Tradução da fatura da Cora para o contrato interno.

        O formato surpreende em dois pontos, ambos confirmados contra o
        ambiente de stage:

        - o Pix NÃO vem em `payment_options.pix`, e sim na RAIZ, em
          `pix.emv` (o copia-e-cola);
        - numa cobrança Pix, `payment_options.bank_slip.url` deixa de ser o
          PDF do boleto e passa a ser o PNG do QR Code, com barcode e linha
          digitável nulos.

        É por isso que o método pedido decide para onde a URL vai.  */
    internal static CobrancaCriada Mapear(JsonElement raiz, MetodoPagamento? metodo)
    {
        var boleto = Filho(Filho(raiz, "payment_options"), "bank_slip");
        var emv = Texto(Filho(raiz, "pix"), "emv");
        var url = Texto(boleto, "url");
        var digitable = Texto(boleto, "digitable");

        /*  Na consulta não há método pedido: a própria fatura denuncia qual
            é — Pix tem emv, boleto tem linha digitável.  */
        var efetivo = metodo ?? (emv is not null ? MetodoPagamento.Pix : MetodoPagamento.Boleto);
        var ehPix = efetivo is MetodoPagamento.Pix;

        return new CobrancaCriada(
            Provider: PaymentProvider.Cora,
            ChargeId: Texto(raiz, "id") ?? string.Empty,
            Metodo: efetivo,
            Status: Texto(raiz, "status") ?? "OPEN",
            LinkPagamento: url,
            BoletoUrl: ehPix ? null : url,
            BoletoLinhaDigitavel: ehPix ? null : digitable,
            BoletoCodigoBarras: ehPix ? null : Texto(boleto, "barcode"),
            PixCopiaCola: emv,
            PixQrCodeUrl: ehPix ? url : null,
            ValorPago: Reais(raiz, "total_paid"));
    }

    /*  UUIDv5 (RFC 4122): mesmo texto → mesmo UUID, em qualquer instância da
        API.  Sem isso, dois pods processando a mesma parcela gerariam duas
        faturas.  */
    internal static Guid ChaveIdempotencia(string referencia)
    {
        /*  Namespace fixo do projeto (UUIDv4 sorteado uma vez).  */
        var ns = new Guid("9f3f0b2e-7a1d-4a5e-9c2f-6b8d41f0c7a3").ToByteArray();
        InverterCamposParaBigEndian(ns);

        var nome = Encoding.UTF8.GetBytes(referencia ?? string.Empty);
        var hash = SHA1.HashData([.. ns, .. nome]);

        var uuid = hash[..16];
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50);  // versão 5
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);  // variante RFC 4122

        InverterCamposParaBigEndian(uuid);
        return new Guid(uuid);
    }

    /*  Guid do .NET guarda os três primeiros campos em little-endian; a RFC
        exige big-endian tanto na entrada quanto na saída do hash.  */
    private static void InverterCamposParaBigEndian(byte[] uuid)
    {
        Array.Reverse(uuid, 0, 4);
        Array.Reverse(uuid, 4, 2);
        Array.Reverse(uuid, 6, 2);
    }

    /*  O corpo de erro do PSP pode trazer dados do pagador; vai truncado
        para o log não virar depósito de PII.  */
    internal static string Resumir(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var limpo = body.Trim();
        return limpo.Length <= 300 ? limpo : string.Concat(limpo.AsSpan(0, 300), "…[truncado]");
    }

    private static string PaymentForm(MetodoPagamento metodo) => metodo switch
    {
        MetodoPagamento.Boleto => "BANK_SLIP",
        MetodoPagamento.Pix    => "PIX",
        _                      => throw new PaymentGatewayException(PaymentProvider.Cora, $"Método {metodo} não mapeado.")
    };

    private static string Digitos(string valor) => new((valor ?? string.Empty).Where(char.IsDigit).ToArray());

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

    private static decimal? Reais(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
            && elemento.TryGetProperty(propriedade, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out var centavos)
            ? centavos / 100m
            : null;
}
