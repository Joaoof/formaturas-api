using System.Text.Json;
using FormaturasFlow.Api.Asaas;
using FormaturasFlow.Api.Cora;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Pagamentos;

public static class PagamentoEndpoints
{
    public record CriarCobrancaRequest(string Tipo, int? NumParcelasCartao);

    public static IEndpointRouteBuilder MapPagamentoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/parcelas/{id:guid}/cobranca", CriarCobrancaAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithTags("Pagamentos")
            .WithSummary("Emite cobranca (PIX, boleto ou cartao) roteando para o PSP correto")
            .WithDescription("""
                O PSP e escolhido automaticamente pelo tipo de evento da turma:
                - Turma tipo Formatura/Outro: Cora para PIX e boleto, Asaas para cartao
                - Turma tipo Casamento: Asaas para tudo (permite agendar datas futuras)

                Body: `{ "tipo": "pix" | "boleto" | "cartao", "numParcelasCartao": 3 (opcional) }`
                Idempotencia: se a parcela ja tem `pspChargeId`, retorna a mesma cobranca.
                """)
            .Produces<Parcela>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status403Forbidden);

        app.MapPost("/webhooks/asaas", WebhookAsaasAsync)
            .AllowAnonymous()
            .WithTags("Pagamentos")
            .WithSummary("Webhook publico do Asaas — nao chame direto")
            .WithDescription("Chamado quando um pagamento e recebido/confirmado. Autenticacao via header `asaas-access-token`. Idempotente por eventId.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost("/webhooks/cora", WebhookCoraAsync)
            .AllowAnonymous()
            .WithTags("Pagamentos")
            .WithSummary("Webhook publico da Cora — nao chame direto")
            .WithDescription("Chamado quando uma invoice muda de status. Autenticacao via header `x-cora-signature`. Idempotente por eventId.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> CriarCobrancaAsync(
        Guid id,
        [FromBody] CriarCobrancaRequest req,
        AppDbContext db,
        PagamentoService service,
        CancellationToken ct)
    {
        if (!Enum.TryParse<TipoPagamento>(req.Tipo, ignoreCase: true, out var tipo))
            return Results.BadRequest(new { erro = "tipo invalido. Use 'pix', 'boleto' ou 'cartao'." });

        var parcela = await db.Parcelas
            .Include(p => p.Contrato)!.ThenInclude(c => c!.Aluno)!.ThenInclude(a => a!.Turma)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (parcela is null) return Results.NotFound();
        if (parcela.Status == StatusParcela.Pago)
            return Results.Conflict(new { erro = "Parcela ja quitada." });
        if (!string.IsNullOrEmpty(parcela.PspChargeId))
            return Results.Ok(new { existente = true, parcela });

        var atualizada = await service.EmitirCobrancaAsync(parcela, tipo, req.NumParcelasCartao, ct);
        return Results.Ok(atualizada);
    }

    private static async Task<IResult> WebhookAsaasAsync(
        HttpContext ctx,
        AppDbContext db,
        IOptions<AsaasOptions> opt,
        CancellationToken ct)
    {
        var token = ctx.Request.Headers["asaas-access-token"].ToString();
        if (!string.Equals(token, opt.Value.WebhookToken, StringComparison.Ordinal))
            return Results.Unauthorized();

        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var eventId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var eventType = root.TryGetProperty("event", out var evEl) ? evEl.GetString() ?? "unknown" : "unknown";

        if (await db.WebhookEvents.AnyAsync(w => w.Provider == "asaas" && w.EventId == eventId, ct))
            return Results.Ok(new { duplicado = true });

        db.WebhookEvents.Add(new WebhookEvent
        {
            Provider = "asaas",
            EventId = eventId,
            EventType = eventType,
            PayloadJson = body,
            ProcessadoEm = DateTimeOffset.UtcNow
        });

        if (root.TryGetProperty("payment", out var pay))
        {
            var chargeId = pay.TryGetProperty("id", out var ci) ? ci.GetString() : null;
            var status = pay.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (chargeId is not null)
                await AtualizarParcelaAsync(db, "asaas", chargeId, status, pay, ct);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> WebhookCoraAsync(
        HttpContext ctx,
        AppDbContext db,
        IOptions<CoraOptions> opt,
        CancellationToken ct)
    {
        var sig = ctx.Request.Headers["x-cora-signature"].ToString();
        if (!string.Equals(sig, opt.Value.WebhookToken, StringComparison.Ordinal))
            return Results.Unauthorized();

        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var eventId = root.TryGetProperty("eventId", out var e) ? e.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() ?? "unknown" : "unknown";

        if (await db.WebhookEvents.AnyAsync(w => w.Provider == "cora" && w.EventId == eventId, ct))
            return Results.Ok(new { duplicado = true });

        db.WebhookEvents.Add(new WebhookEvent
        {
            Provider = "cora",
            EventId = eventId,
            EventType = eventType,
            PayloadJson = body,
            ProcessadoEm = DateTimeOffset.UtcNow
        });

        var invoiceEl = root.TryGetProperty("invoice", out var inv) ? inv
            : root.TryGetProperty("resource", out var rs) ? rs
            : root;
        var chargeId = invoiceEl.TryGetProperty("id", out var ci) ? ci.GetString() : null;
        var status = invoiceEl.TryGetProperty("status", out var st) ? st.GetString() : null;
        if (chargeId is not null)
            await AtualizarParcelaAsync(db, "cora", chargeId, status, invoiceEl, ct);

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { ok = true });
    }

    private static async Task AtualizarParcelaAsync(
        AppDbContext db, string provider, string chargeId, string? status, JsonElement payload, CancellationToken ct)
    {
        var parcela = await db.Parcelas.FirstOrDefaultAsync(
            p => p.PspProvider == provider && p.PspChargeId == chargeId, ct);
        if (parcela is null) return;

        parcela.PspStatus = status;
        parcela.AtualizadaEm = DateTimeOffset.UtcNow;

        if (EhStatusPago(provider, status))
        {
            parcela.Status = StatusParcela.Pago;
            parcela.DataPagamento = DateOnly.FromDateTime(DateTime.UtcNow);
            var valor = payload.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDecimal()
                : parcela.Valor;
            parcela.ValorPago = valor;
        }
    }

    private static bool EhStatusPago(string provider, string? status) => (provider, status) switch
    {
        ("asaas", "RECEIVED") => true,
        ("asaas", "CONFIRMED") => true,
        ("asaas", "RECEIVED_IN_CASH") => true,
        ("cora", "PAID") => true,
        ("cora", "PAID_MANUALLY") => true,
        _ => false
    };
}
