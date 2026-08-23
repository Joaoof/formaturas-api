using System.Text.Json;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Efi;

public static class EfiEndpoints
{
    public record CriarCobrancaRequest(string Tipo);

    public static IEndpointRouteBuilder MapEfiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/parcelas/{id:guid}/cobranca", CriarCobrancaAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithTags("Efi");

        app.MapPost("/webhooks/efi", WebhookEfiAsync)
            .AllowAnonymous()
            .WithTags("Efi");

        return app;
    }

    private static async Task<IResult> CriarCobrancaAsync(
        Guid id,
        [FromBody] CriarCobrancaRequest req,
        AppDbContext db,
        EfiClient efi,
        CancellationToken ct)
    {
        var parcela = await db.Parcelas
            .Include(p => p.Contrato)!.ThenInclude(c => c!.Aluno)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (parcela is null) return Results.NotFound();
        if (parcela.Status == StatusParcela.Pago) return Results.Conflict(new { erro = "Parcela já quitada." });
        if (!string.IsNullOrEmpty(parcela.PspChargeId))
            return Results.Ok(new { existente = true, parcela });

        var aluno = parcela.Contrato?.Aluno ?? throw new InvalidOperationException("Aluno ausente.");
        var descricao = $"Parcela {parcela.Numero} · {parcela.Contrato?.Pacote ?? "Serviço"}";

        if (req.Tipo.Equals("boleto", StringComparison.OrdinalIgnoreCase))
        {
            var boleto = await efi.CriarBoletoAsync(new EfiClient.CriarBoletoRequest(
                Valor: parcela.Valor,
                Vencimento: parcela.Vencimento,
                PayerNome: aluno.NomeCompleto,
                PayerCpf: aluno.Cpf ?? "00000000000",
                Descricao: descricao), ct);

            parcela.PspProvider = "efi";
            parcela.PspChargeId = boleto.ChargeId;
            parcela.PspStatus = boleto.Status;
            parcela.BoletoUrl = boleto.BoletoUrl;
            parcela.BoletoLinhaDigitavel = boleto.LinhaDigitavel;
            parcela.BoletoCodigoBarras = boleto.CodigoBarras;
            parcela.LinkPagamento = boleto.BoletoUrl;
        }
        else if (req.Tipo.Equals("pix", StringComparison.OrdinalIgnoreCase))
        {
            var txId = $"parc{parcela.Id:N}"[..Math.Min(35, ("parc" + parcela.Id.ToString("N")).Length)];
            var pix = await efi.CriarPixAsync(new EfiClient.CriarPixRequest(
                TxId: txId,
                Valor: parcela.Valor,
                PayerCpf: aluno.Cpf ?? "00000000000",
                PayerNome: aluno.NomeCompleto,
                Descricao: descricao), ct);

            parcela.PspProvider = "efi";
            parcela.PspChargeId = pix.TxId;
            parcela.PspStatus = pix.Status;
            parcela.PixCopiaCola = pix.CopiaCola;
            parcela.PixQrCodeUrl = pix.QrCodeUrl;
            parcela.LinkPagamento = pix.QrCodeUrl;
        }
        else
        {
            return Results.BadRequest(new { erro = "Tipo inválido. Use 'boleto' ou 'pix'." });
        }

        parcela.AtualizadaEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(parcela);
    }

    private static async Task<IResult> WebhookEfiAsync(
        HttpContext ctx,
        AppDbContext db,
        IOptions<EfiOptions> opt,
        CancellationToken ct)
    {
        var secretRecebido = ctx.Request.Query["secret"].ToString();
        if (!string.Equals(secretRecebido, opt.Value.WebhookSecret, StringComparison.Ordinal))
            return Results.Unauthorized();

        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var eventId = doc.RootElement.TryGetProperty("evento", out var ev) && ev.TryGetProperty("id", out var eid)
            ? eid.ToString()
            : Guid.NewGuid().ToString();
        var eventType = doc.RootElement.TryGetProperty("notification", out var n) ? n.ToString() : "unknown";

        var jaExiste = await db.WebhookEvents
            .AnyAsync(w => w.Provider == "efi" && w.EventId == eventId, ct);
        if (jaExiste) return Results.Ok(new { duplicado = true });

        var registro = new WebhookEvent
        {
            Provider = "efi",
            EventId = eventId,
            EventType = eventType,
            PayloadJson = body
        };
        db.WebhookEvents.Add(registro);

        if (doc.RootElement.TryGetProperty("pix", out var pixArr) && pixArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var pix in pixArr.EnumerateArray())
            {
                var txid = pix.GetProperty("txid").GetString();
                var valorPago = decimal.Parse(pix.GetProperty("valor").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);

                var parcela = await db.Parcelas.FirstOrDefaultAsync(p => p.PspChargeId == txid, ct);
                if (parcela is null) continue;

                parcela.Status = StatusParcela.Pago;
                parcela.ValorPago = valorPago;
                parcela.DataPagamento = DateOnly.FromDateTime(DateTime.UtcNow);
                parcela.PspStatus = "CONCLUIDA";
                parcela.AtualizadaEm = DateTimeOffset.UtcNow;
            }
        }

        registro.ProcessadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }
}
