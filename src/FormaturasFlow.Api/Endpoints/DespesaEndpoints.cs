using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class DespesaEndpoints
{
    public record DespesaCreate(
        string Descricao, string? Categoria, decimal Valor,
        DateOnly Vencimento, string? FormaPagamento, string? Observacao,
        Guid? TurmaId, DateOnly? DataPagamento, bool? MarcarPago);

    public record DespesaBaixa(DateOnly? DataPagamento, string? FormaPagamento);

    public record DespesaUpdate(
        string Descricao, string? Categoria, decimal Valor,
        DateOnly Vencimento, string? FormaPagamento, string? Observacao,
        Guid? TurmaId, DateOnly? DataPagamento, bool? MarcarPago);

    public static IEndpointRouteBuilder MapDespesaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/despesas").WithTags("Despesas").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db) =>
            await db.Despesas.AsNoTracking().OrderBy(d => d.Vencimento).ToListAsync())
            .WithSummary("Lista despesas ordenadas por vencimento")
            .Produces<Despesa[]>(StatusCodes.Status200OK);

        group.MapPost("/", async (DespesaCreate req, AppDbContext db) =>
        {
            var d = new Despesa
            {
                Descricao = req.Descricao,
                Categoria = req.Categoria ?? "geral",
                Valor = req.Valor,
                Vencimento = req.Vencimento,
                FormaPagamento = req.FormaPagamento,
                Observacao = req.Observacao,
                TurmaId = req.TurmaId,
                DataPagamento = req.DataPagamento,
                Status = req.MarcarPago == true ? StatusDespesa.Pago : StatusDespesa.Pendente
            };
            db.Despesas.Add(d);
            await db.SaveChangesAsync();
            return Results.Created($"/despesas/{d.Id}", d);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria uma despesa")
            .Produces<Despesa>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async (Guid id, DespesaUpdate req, AppDbContext db) =>
        {
            var d = await db.Despesas.FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return Results.NotFound();

            d.Descricao = req.Descricao;
            d.Categoria = req.Categoria ?? d.Categoria;
            d.Valor = req.Valor;
            d.Vencimento = req.Vencimento;
            d.FormaPagamento = req.FormaPagamento;
            d.Observacao = req.Observacao;
            d.TurmaId = req.TurmaId;
            if (req.MarcarPago == true)
            {
                d.Status = StatusDespesa.Pago;
                d.DataPagamento = req.DataPagamento ?? DateOnly.FromDateTime(DateTime.UtcNow);
            }
            d.AtualizadaEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(d);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Atualiza uma despesa")
            .Produces<Despesa>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/baixar", async (Guid id, DespesaBaixa req, AppDbContext db) =>
        {
            var d = await db.Despesas.FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return Results.NotFound();
            d.Status = StatusDespesa.Pago;
            d.DataPagamento = req.DataPagamento ?? DateOnly.FromDateTime(DateTime.UtcNow);
            if (req.FormaPagamento is not null) d.FormaPagamento = req.FormaPagamento;
            d.AtualizadaEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(d);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Marca despesa como paga")
            .Produces<Despesa>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/desfazer", async (Guid id, AppDbContext db) =>
        {
            var d = await db.Despesas.FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return Results.NotFound();
            d.Status = StatusDespesa.Pendente;
            d.DataPagamento = null;
            d.AtualizadaEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(d);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Reverte baixa de despesa")
            .Produces<Despesa>(StatusCodes.Status200OK);

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var deleted = await db.Despesas.Where(x => x.Id == id).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin))
            .WithSummary("Remove despesa")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
