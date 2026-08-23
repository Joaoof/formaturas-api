using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class ParcelaEndpoints
{
    public record ParcelaBaixa(decimal? ValorPago, DateOnly? DataPagamento, string? FormaPagamento);

    public static IEndpointRouteBuilder MapParcelaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/parcelas").WithTags("Parcelas").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, [FromQuery] string? status) =>
        {
            var q = db.Parcelas.AsNoTracking();
            if (Enum.TryParse<StatusParcela>(status, ignoreCase: true, out var s))
                q = q.Where(p => p.Status == s);
            return await q.OrderBy(p => p.Vencimento).ToListAsync();
        })
            .WithSummary("Lista todas as parcelas ordenadas por vencimento")
            .WithDescription("Aceita filtro opcional `?status=Pendente|Pago|Atrasado|Cancelado` (case-insensitive).")
            .Produces<Parcela[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/baixar", BaixarAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Marca a parcela como paga (baixa manual)")
            .WithDescription("""
                Usado quando o pagamento chega fora do PSP (dinheiro, transferência).
                Se `valorPago` não vier, assume o valor cheio da parcela.
                Se `dataPagamento` não vier, usa a data atual (UTC).
                Requer papel `super_admin` ou `funcionario`.
                """)
            .Produces<Parcela>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/desfazer", DesfazerAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Reverte uma baixa (parcela volta a Pendente)")
            .WithDescription("Zera `valorPago` e `dataPagamento`. Requer papel `super_admin` ou `funcionario`.")
            .Produces<Parcela>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> BaixarAsync(Guid id, [FromBody] ParcelaBaixa req, AppDbContext db)
    {
        var p = await db.Parcelas.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return Results.NotFound();

        p.Status = StatusParcela.Pago;
        p.ValorPago = req.ValorPago ?? p.Valor;
        p.DataPagamento = req.DataPagamento ?? DateOnly.FromDateTime(DateTime.UtcNow);
        p.FormaPagamento = req.FormaPagamento ?? p.FormaPagamento;
        p.AtualizadaEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(p);
    }

    private static async Task<IResult> DesfazerAsync(Guid id, AppDbContext db)
    {
        var p = await db.Parcelas.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return Results.NotFound();

        p.Status = StatusParcela.Pendente;
        p.ValorPago = 0;
        p.DataPagamento = null;
        p.AtualizadaEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(p);
    }
}
