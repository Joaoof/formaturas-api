using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class ContratoEndpoints
{
    public record ContratoCreate(
        Guid AlunoId,
        string? Pacote,
        decimal ValorTotal,
        decimal ValorEntrada,
        int NumParcelas,
        string? FormaPagamento,
        DateOnly DataContrato,
        DateOnly PrimeiroVencimento);

    public static IEndpointRouteBuilder MapContratoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contratos").WithTags("Contratos").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db) =>
            await db.Contratos.AsNoTracking().ToListAsync());

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var c = await db.Contratos.Include(x => x.Parcelas).AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
            return c is null ? Results.NotFound() : Results.Ok(c);
        });

        group.MapPost("/", CreateWithParcelasAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario));

        return app;
    }

    private static async Task<IResult> CreateWithParcelasAsync(
        [FromBody] ContratoCreate req, AppDbContext db)
    {
        if (req.NumParcelas < 1)
            return Results.BadRequest(new { erro = "num_parcelas deve ser >= 1." });

        var alunoExiste = await db.Alunos.AnyAsync(a => a.Id == req.AlunoId);
        if (!alunoExiste) return Results.NotFound(new { erro = "Aluno não encontrado." });

        var saldo = req.ValorTotal - req.ValorEntrada;
        var valorParcela = Math.Round(saldo / req.NumParcelas, 2);
        var resto = saldo - (valorParcela * req.NumParcelas);

        await using var tx = await db.Database.BeginTransactionAsync();

        var contrato = new Contrato
        {
            AlunoId = req.AlunoId,
            Pacote = req.Pacote,
            ValorTotal = req.ValorTotal,
            ValorEntrada = req.ValorEntrada,
            NumParcelas = req.NumParcelas,
            FormaPagamento = req.FormaPagamento,
            DataContrato = req.DataContrato
        };
        db.Contratos.Add(contrato);

        for (var i = 1; i <= req.NumParcelas; i++)
        {
            var venc = req.PrimeiroVencimento.AddMonths(i - 1);
            var valor = valorParcela + (i == req.NumParcelas ? resto : 0);
            db.Parcelas.Add(new Parcela
            {
                ContratoId = contrato.Id,
                Numero = i,
                Valor = valor,
                Vencimento = venc,
                Status = StatusParcela.Pendente
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Results.Created($"/contratos/{contrato.Id}", contrato);
    }
}
