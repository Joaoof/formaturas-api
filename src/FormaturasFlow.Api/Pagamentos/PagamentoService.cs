using FormaturasFlow.Api.Asaas;
using FormaturasFlow.Api.Cora;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Pagamentos;

public enum TipoPagamento { Pix, Boleto, Cartao, Checkout }

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
        (TipoPagamento.Checkout, _) => Provider.Asaas,
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
            TipoPagamento.Checkout => AsaasClient.BillingType.UNDEFINED,
            _ => AsaasClient.BillingType.UNDEFINED
        };

        var parcelar = tipo == TipoPagamento.Cartao && numParcelas is > 1;
        var cobranca = await asaas.CriarCobrancaAsync(new AsaasClient.CriarCobrancaRequest(
            Customer: aluno.AsaasCustomerId!,
            BillingType: billing,
            Value: p.Valor,
            DueDate: p.Vencimento,
            Description: descricao,
            ExternalReference: p.Id.ToString(),
            InstallmentCount: parcelar ? numParcelas : null,
            InstallmentValue: parcelar ? Math.Round(p.Valor / numParcelas!.Value, 2) : null), ct);

        p.PspProvider = "asaas";
        p.PspChargeId = cobranca.Id;
        p.PspStatus = cobranca.Status;
        p.LinkPagamento = cobranca.InvoiceUrl;

        if (tipo == TipoPagamento.Boleto)
        {
            p.BoletoUrl = cobranca.BankSlipUrl ?? cobranca.InvoiceUrl;
            p.BoletoLinhaDigitavel = cobranca.IdentificationField;
            if (string.IsNullOrEmpty(p.BoletoLinhaDigitavel))
                await CompletarBoletoAsaasAsync(p, cobranca.Id, ct);
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

    private async Task CompletarBoletoAsaasAsync(Parcela p, string paymentId, CancellationToken ct)
    {
        for (var tentativa = 1; tentativa <= 3; tentativa++)
        {
            await Task.Delay(TimeSpan.FromSeconds(tentativa * 2), ct);
            var info = await asaas.BuscarBoletoIdentificationAsync(paymentId, ct);
            if (info is null || string.IsNullOrEmpty(info.IdentificationField)) continue;
            p.BoletoLinhaDigitavel = info.IdentificationField;
            p.BoletoCodigoBarras = info.BarCode;
            return;
        }
        log.LogWarning("Boleto Asaas {Id} sem linha digitavel apos 3 tentativas; webhook PAYMENT_UPDATED devera preencher depois", paymentId);
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

    public async Task<Cobranca> EmitirCobrancaStandaloneAsync(Cobranca c, TipoPagamento tipo, int? numParcelasCartao, CancellationToken ct)
    {
        var eCasamento = c.TipoEvento == TipoEvento.Casamento;
        var provider = EscolherProvider(tipo, eCasamento);

        if (provider == Provider.Asaas)
            await EmitirStandaloneViaAsaasAsync(c, tipo, numParcelasCartao, ct);
        else
            await EmitirStandaloneViaCoraAsync(c, tipo, ct);

        c.TipoPagamento = tipo.ToString().ToLowerInvariant();
        c.NumParcelasCartao = numParcelasCartao;
        c.AtualizadaEm = DateTimeOffset.UtcNow;
        db.Cobrancas.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    private async Task EmitirStandaloneViaAsaasAsync(Cobranca c, TipoPagamento tipo, int? numParcelas, CancellationToken ct)
    {
        var customerId = c.PspCustomerId;
        if (string.IsNullOrEmpty(customerId))
        {
            var cliente = await asaas.CriarClienteAsync(new AsaasClient.CriarClienteRequest(
                Name: c.ClienteNome,
                CpfCnpj: c.ClienteCpf ?? "00000000000",
                Email: c.ClienteEmail,
                Phone: c.ClienteTelefone,
                MobilePhone: c.ClienteWhatsapp), ct);
            customerId = cliente.Id;
            c.PspCustomerId = customerId;
        }

        var billing = tipo switch
        {
            TipoPagamento.Cartao => AsaasClient.BillingType.CREDIT_CARD,
            TipoPagamento.Boleto => AsaasClient.BillingType.BOLETO,
            TipoPagamento.Pix => AsaasClient.BillingType.PIX,
            TipoPagamento.Checkout => AsaasClient.BillingType.UNDEFINED,
            _ => AsaasClient.BillingType.UNDEFINED
        };

        var parcelar = tipo == TipoPagamento.Cartao && numParcelas is > 1;
        var cobranca = await asaas.CriarCobrancaAsync(new AsaasClient.CriarCobrancaRequest(
            Customer: customerId!,
            BillingType: billing,
            Value: c.Valor,
            DueDate: c.Vencimento,
            Description: c.Descricao,
            ExternalReference: c.ExternalReference,
            InstallmentCount: parcelar ? numParcelas : null,
            InstallmentValue: parcelar ? Math.Round(c.Valor / numParcelas!.Value, 2) : null), ct);

        c.PspProvider = "asaas";
        c.PspChargeId = cobranca.Id;
        c.PspStatus = cobranca.Status;
        c.LinkPagamento = cobranca.InvoiceUrl;

        if (tipo == TipoPagamento.Boleto)
        {
            c.BoletoUrl = cobranca.BankSlipUrl ?? cobranca.InvoiceUrl;
            c.BoletoLinhaDigitavel = cobranca.IdentificationField;
            if (string.IsNullOrEmpty(c.BoletoLinhaDigitavel))
            {
                for (var tentativa = 1; tentativa <= 3; tentativa++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(tentativa * 2), ct);
                    var info = await asaas.BuscarBoletoIdentificationAsync(cobranca.Id, ct);
                    if (info is null || string.IsNullOrEmpty(info.IdentificationField)) continue;
                    c.BoletoLinhaDigitavel = info.IdentificationField;
                    c.BoletoCodigoBarras = info.BarCode;
                    break;
                }
            }
        }
        else if (tipo == TipoPagamento.Pix)
        {
            try
            {
                var qr = await asaas.BuscarPixQrCodeAsync(cobranca.Id, ct);
                c.PixCopiaCola = qr.Payload;
                c.PixQrCodeUrl = qr.EncodedImage;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Cobranca standalone Asaas PIX criada mas QR falhou. Id={Id}", cobranca.Id);
            }
        }
    }

    private async Task EmitirStandaloneViaCoraAsync(Cobranca c, TipoPagamento tipo, CancellationToken ct)
    {
        var kind = tipo == TipoPagamento.Pix ? CoraClient.InvoiceKind.PIX : CoraClient.InvoiceKind.BOLETO;
        var invoice = await cora.CriarInvoiceAsync(new CoraClient.CriarInvoiceRequest(
            Kind: kind,
            Value: c.Valor,
            DueDate: c.Vencimento,
            CustomerName: c.ClienteNome,
            CustomerDocument: c.ClienteCpf ?? "00000000000",
            CustomerEmail: c.ClienteEmail,
            Description: c.Descricao), ct);

        c.PspProvider = "cora";
        c.PspChargeId = invoice.Id;
        c.PspStatus = invoice.Status;

        if (tipo == TipoPagamento.Pix)
        {
            c.PixCopiaCola = invoice.PixEmv;
            c.PixQrCodeUrl = invoice.PixQrCodeBase64;
            c.LinkPagamento = invoice.PixEmv;
        }
        else
        {
            c.BoletoUrl = invoice.BoletoUrl;
            c.BoletoLinhaDigitavel = invoice.BoletoLinhaDigitavel;
            c.BoletoCodigoBarras = invoice.BoletoCodigoBarras;
            c.LinkPagamento = invoice.BoletoUrl;
        }
    }

    private enum Provider { Asaas, Cora }
}
