using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

/*  Adapter do Asaas (PSP dos Casamentos).

    `Suporta` lista o que o Asaas tecnicamente processa — cartão, boleto
    e Pix.  O fato de Pix estar aqui e não estar habilitado para nenhum
    domínio hoje é proposital: no dia em que o negócio liberar Pix para
    Casamento, muda-se a PaymentRoutingPolicy e o caminho já existe.  */
public sealed class AsaasPaymentGateway(
    HttpClient http,
    IOptions<AsaasOptions> opt,
    ILogger<AsaasPaymentGateway> log) : IPaymentGateway
{
    private readonly AsaasOptions _opt = opt.Value;

    public PaymentProvider Provider => PaymentProvider.Asaas;

    public bool Suporta(MetodoPagamento metodo) => metodo is
        MetodoPagamento.Boleto or MetodoPagamento.Pix or MetodoPagamento.CartaoCredito;

    public async Task<CobrancaCriada> CriarCobrancaAsync(CobrancaRequest req, CancellationToken ct = default)
    {
        http.DefaultRequestHeaders.Remove("access_token");
        http.DefaultRequestHeaders.Add("access_token", _opt.ApiKey);

        var payload = new Dictionary<string, object?>
        {
            ["customer"]          = await ObterOuCriarClienteAsync(req.Pagador, ct),
            ["billingType"]       = BillingType(req.Metodo),
            ["value"]             = req.Valor,
            ["dueDate"]           = req.Vencimento.ToString("yyyy-MM-dd"),
            ["description"]       = req.Descricao,
            ["externalReference"] = req.ReferenciaExterna
        };

        if (req.Metodo == MetodoPagamento.CartaoCredito)
        {
            var cartao = req.Cartao
                ?? throw new DadosPagamentoIncompletosException("cartao",
                    "Pagamento com cartão exige os dados do portador.");

            var cep = req.Pagador.Cep
                ?? throw new DadosPagamentoIncompletosException("pagador.cep",
                    "O Asaas exige CEP do portador para transações com cartão.");

            var numero = req.Pagador.NumeroEndereco
                ?? throw new DadosPagamentoIncompletosException("pagador.numeroEndereco",
                    "O Asaas exige o número do endereço do portador para transações com cartão.");

            payload["creditCard"] = new
            {
                holderName  = cartao.Titular,
                number      = cartao.Numero,
                expiryMonth = cartao.MesValidade.ToString("00"),
                expiryYear  = cartao.AnoValidade.ToString("0000"),
                ccv         = cartao.Cvv
            };

            payload["creditCardHolderInfo"] = new
            {
                name          = req.Pagador.Nome,
                email         = req.Pagador.Email,
                cpfCnpj       = Digitos(req.Pagador.Documento),
                postalCode    = Digitos(cep),
                addressNumber = numero,
                phone         = req.Pagador.Telefone
            };
        }

        using var resp = await http.PostAsJsonAsync($"{_opt.BaseUrl}/payments", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Asaas cobrança falhou: {Status} {Body}", resp.StatusCode, body);
            throw new PaymentGatewayException(Provider, $"Falha ao criar cobrança no Asaas: {resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var raiz = doc.RootElement;

        var chargeId = Texto(raiz, "id") ?? string.Empty;
        var status   = Texto(raiz, "status") ?? "PENDING";
        var link     = Texto(raiz, "invoiceUrl");

        var criada = new CobrancaCriada(
            Provider: Provider,
            ChargeId: chargeId,
            Metodo: req.Metodo,
            Status: status,
            LinkPagamento: link,
            BoletoUrl: Texto(raiz, "bankSlipUrl"),
            BoletoLinhaDigitavel: Texto(raiz, "identificationField"),
            BoletoCodigoBarras: Texto(raiz, "nossoNumero"));

        if (req.Metodo != MetodoPagamento.Pix)
            return criada;

        /*  No Asaas o QR Code do Pix sai em um recurso separado.  */
        using var qrResp = await http.GetAsync($"{_opt.BaseUrl}/payments/{chargeId}/pixQrCode", ct);
        var qrBody = await qrResp.Content.ReadAsStringAsync(ct);
        if (!qrResp.IsSuccessStatusCode)
        {
            log.LogError("Asaas QR Code falhou: {Status} {Body}", qrResp.StatusCode, qrBody);
            throw new PaymentGatewayException(Provider, $"Cobrança criada, mas QR Code indisponível: {qrResp.StatusCode}");
        }

        using var qrDoc = JsonDocument.Parse(qrBody);
        var imagem = Texto(qrDoc.RootElement, "encodedImage");

        return criada with
        {
            PixCopiaCola = Texto(qrDoc.RootElement, "payload"),
            PixQrCodeUrl = imagem is null ? null : $"data:image/png;base64,{imagem}"
        };
    }

    /*  O Asaas exige um customer antes da cobrança; reaproveitamos pelo
        CPF/CNPJ para não duplicar cadastro a cada parcela.  */
    private async Task<string> ObterOuCriarClienteAsync(PagadorInfo pagador, CancellationToken ct)
    {
        var documento = Digitos(pagador.Documento);

        using var busca = await http.GetAsync($"{_opt.BaseUrl}/customers?cpfCnpj={documento}", ct);
        if (busca.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(await busca.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.GetArrayLength() > 0)
            {
                var existente = Texto(data[0], "id");
                if (existente is not null) return existente;
            }
        }

        using var resp = await http.PostAsJsonAsync($"{_opt.BaseUrl}/customers", new
        {
            name         = pagador.Nome,
            cpfCnpj      = documento,
            email        = pagador.Email,
            mobilePhone  = pagador.Telefone,
            postalCode   = pagador.Cep is null ? null : Digitos(pagador.Cep),
            addressNumber = pagador.NumeroEndereco
        }, ct);

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogError("Asaas cadastro de cliente falhou: {Status} {Body}", resp.StatusCode, body);
            throw new PaymentGatewayException(Provider, $"Falha ao cadastrar pagador no Asaas: {resp.StatusCode}");
        }

        using var criado = JsonDocument.Parse(body);
        return Texto(criado.RootElement, "id")
            ?? throw new PaymentGatewayException(Provider, "Asaas devolveu cliente sem id.");
    }

    private static string BillingType(MetodoPagamento metodo) => metodo switch
    {
        MetodoPagamento.Boleto        => "BOLETO",
        MetodoPagamento.Pix           => "PIX",
        MetodoPagamento.CartaoCredito => "CREDIT_CARD",
        _                             => throw new PaymentGatewayException(PaymentProvider.Asaas, $"Método {metodo} não mapeado.")
    };

    private static string Digitos(string valor) => new(valor.Where(char.IsDigit).ToArray());

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
