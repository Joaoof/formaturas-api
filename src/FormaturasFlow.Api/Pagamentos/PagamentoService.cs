using FormaturasFlow.Api.Asaas;
using FormaturasFlow.Api.Cora;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Pagamentos;

public enum TipoPagamento { Pix, Boleto, Cartao }

public class PagamentoService(
    AppDbContext db,
    AsaasClient asaas,
    CoraClient cora,
    ILogger<PagamentoService> log)
{
    public async Task<Parcela> EmitirCobrancaAsync(
        Parcela parcela,
        TipoPagamento tipo,
        int? numParcelasCartao,
        CancellationToken ct)
    {
        if (parcela.Contrato is null || parcela.Contrato.Aluno is null || parcela.Contrato.Aluno.Turma is null)
            throw new InvalidOperationException("Parcela precisa vir com Contrato.Aluno.Turma includidos.");

        var aluno = parcela.Contrato.Aluno;
        var turma = aluno.Turma!;
        var descricao = $"Parcela {parcela.Numero} - {turma.Nome}";
        var eCasamento = turma.TipoEvento == TipoEvento.Casamento;
        var provider = EscolherProvider(tipo, eCasamento);

        switch (provider)
        {
            case Provider.Asaas:
                await EmitirViaAsaasAsync(parcela, aluno, tipo, descricao, numParcelasCartao, ct);
                break;
            case Provider.Cora:
                await EmitirViaCoraAsync(parcela, aluno, tipo, descricao, ct);
                break;
        }

        parcela.AtualizadaEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return parcela;
    }

    private static Provider EscolherProvider(TipoPagamento tipo, bool eCasamento) => (tipo, eCasamento) switch
    {
        (TipoPagamento.Cartao, _) => Provider.Asaas,
        (_, true) => Provider.Asaas,
        _ => Provider.Cora
    };

    private async Task EmitirViaAsaasAsync(
        Parcela p, Aluno aluno, TipoPagamento tipo, string descricao, int? numParcelas, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(aluno.AsaasCustomerId))
        {
            var cliente = await asaas.CriarClienteAsync(new AsaasClient.CriarClienteRequest(
                Name: aluno.NomeCompleto,
                CpfCnpj: aluno.Cpf ?? "00000000000",
                Email: aluno.Email,
                Phone: aluno.Telefone,
                MobilePhone: aluno.Whatsapp), ct);
            aluno.AsaasCustomerId = cliente.Id;
            aluno.AtualizadoEm = DateTimeOffset.UtcNow;
        }

        var billing = tipo switch
        {
            TipoPagamento.Cartao => AsaasClient.BillingType.CREDIT_CARD,
            TipoPagamento.Boleto => AsaasClient.BillingType.BOLETO,
            TipoPagamento.Pix => AsaasClient.BillingType.PIX,
            _ => AsaasClient.BillingType.UNDEFINED
        };

        var cobranca = await asaas.CriarCobrancaAsync(new AsaasClient.CriarCobrancaRequest(
            Customer: aluno.AsaasCustomerId!,
            BillingType: billing,
            Value: p.Valor,
            DueDate: p.Vencimento,
            Description: descricao,
            ExternalReference: p.Id.ToString(),
            InstallmentCount: tipo == TipoPagamento.Cartao ? numParcelas : null,
            InstallmentValue: tipo == TipoPagamento.Cartao && numParcelas is > 1
                ? Math.Round(p.Valor / numParcelas.Value, 2)
                : null), ct);

        p.PspProvider = "asaas";
        p.PspChargeId = cobranca.Id;
        p.PspStatus = cobranca.Status;
        p.LinkPagamento = cobranca.InvoiceUrl;

        if (tipo == TipoPagamento.Boleto)
        {
            p.BoletoUrl = cobranca.BankSlipUrl ?? cobranca.InvoiceUrl;
            p.BoletoLinhaDigitavel = cobranca.IdentificationField;
        }
        else if (tipo == TipoPagamento.Pix)
        {
            try
            {
                var qr = await asaas.BuscarPixQrCodeAsync(cobranca.Id, ct);
                p.PixCopiaCola = qr.Payload;
                p.PixQrCodeUrl = qr.EncodedImage;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Cobranca Asaas PIX criada mas QR falhou. Id={Id}", cobranca.Id);
            }
        }
    }

    private async Task EmitirViaCoraAsync(
        Parcela p, Aluno aluno, TipoPagamento tipo, string descricao, CancellationToken ct)
    {
        var kind = tipo == TipoPagamento.Pix ? CoraClient.InvoiceKind.PIX : CoraClient.InvoiceKind.BOLETO;

        var invoice = await cora.CriarInvoiceAsync(new CoraClient.CriarInvoiceRequest(
            Kind: kind,
            Value: p.Valor,
            DueDate: p.Vencimento,
            CustomerName: aluno.NomeCompleto,
            CustomerDocument: aluno.Cpf ?? "00000000000",
            CustomerEmail: aluno.Email,
            Description: descricao), ct);

        p.PspProvider = "cora";
        p.PspChargeId = invoice.Id;
        p.PspStatus = invoice.Status;

        if (tipo == TipoPagamento.Pix)
        {
            p.PixCopiaCola = invoice.PixEmv;
            p.PixQrCodeUrl = invoice.PixQrCodeBase64;
            p.LinkPagamento = invoice.PixEmv;
        }
        else
        {
            p.BoletoUrl = invoice.BoletoUrl;
            p.BoletoLinhaDigitavel = invoice.BoletoLinhaDigitavel;
            p.BoletoCodigoBarras = invoice.BoletoCodigoBarras;
            p.LinkPagamento = invoice.BoletoUrl;
        }
    }

    private enum Provider { Asaas, Cora }
}
