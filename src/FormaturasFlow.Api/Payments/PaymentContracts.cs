namespace FormaturasFlow.Api.Payments;

/*  Tenant/domínio de negócio.  É ele — e só ele — que determina o PSP.  */
public enum TipoProjeto
{
    Casamento = 0,
    Formatura = 1
}

public enum MetodoPagamento
{
    Boleto        = 0,
    Pix           = 1,
    CartaoCredito = 2
}

public enum PaymentProvider
{
    Asaas = 0,
    Cora  = 1
}

public record PagadorInfo(
    string  Nome,
    string  Documento,
    string? Email = null,
    string? Telefone = null,
    string? Cep = null,
    string? NumeroEndereco = null);

public record CartaoCreditoInfo(
    string Titular,
    string Numero,
    int    MesValidade,
    int    AnoValidade,
    string Cvv);

public record CobrancaRequest(
    MetodoPagamento    Metodo,
    decimal            Valor,
    DateOnly           Vencimento,
    string             Descricao,
    string             ReferenciaExterna,
    PagadorInfo        Pagador,
    CartaoCreditoInfo? Cartao = null);

/*  Resposta normalizada: o chamador nunca vê o JSON bruto do PSP.  */
public record CobrancaCriada(
    PaymentProvider Provider,
    string          ChargeId,
    MetodoPagamento Metodo,
    string          Status,
    string?         LinkPagamento = null,
    string?         BoletoUrl = null,
    string?         BoletoLinhaDigitavel = null,
    string?         BoletoCodigoBarras = null,
    string?         PixCopiaCola = null,
    string?         PixQrCodeUrl = null);
