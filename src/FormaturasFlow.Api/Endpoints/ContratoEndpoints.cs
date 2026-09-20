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
        decimal? Desconto,
        int NumParcelas,
        int? DiaVencimento,
        bool? AutorizaImagem,
        string? FormaPagamento,
        DateOnly DataContrato,
        DateOnly PrimeiroVencimento);

    public record ContratoUpdate(
        string? Pacote,
        decimal ValorTotal,
        decimal ValorEntrada,
        decimal? Desconto,
        int NumParcelas,
        int? DiaVencimento,
        bool? AutorizaImagem,
        string? FormaPagamento,
        string? TextoContrato,
        bool? RecalcularParcelas,
        DateOnly? PrimeiroVencimento);

    public static IEndpointRouteBuilder MapContratoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contratos").WithTags("Contratos").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, [FromQuery] Guid? alunoId, [FromQuery] Guid? turmaId) =>
        {
            var q = db.Contratos.Include(c => c.Parcelas).AsNoTracking().AsQueryable();
            if (alunoId.HasValue) q = q.Where(c => c.AlunoId == alunoId.Value);
            if (turmaId.HasValue) q = q.Where(c => c.Aluno!.TurmaId == turmaId.Value);
            return await q.ToListAsync();
        })
            .WithSummary("Lista contratos (opcionalmente filtrados por alunoId ou turmaId)")
            .Produces<Contrato[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var c = await db.Contratos.Include(x => x.Parcelas).AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) throw new RecursoNaoEncontradoException("Contrato", id);
            return Results.Ok(c);
        })
            .WithSummary("Detalhe de um contrato com suas parcelas")
            .Produces<Contrato>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateWithParcelasAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria contrato e gera as parcelas do saldo em uma transação atômica")
            .Produces<Contrato>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Atualiza dados de um contrato, opcionalmente recalculando parcelas")
            .Produces<Contrato>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Remove um contrato e suas parcelas")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateWithParcelasAsync(
        [FromBody] ContratoCreate req, AppDbContext db)
    {
        if (req.NumParcelas < 1)
            throw new DadosInvalidosException("CONTRATO_NUM_PARCELAS_INVALIDO", "num_parcelas deve ser >= 1.");

        var alunoExiste = await db.Alunos.AnyAsync(a => a.Id == req.AlunoId);
        if (!alunoExiste) throw new RecursoNaoEncontradoException("Aluno", req.AlunoId);

        var desconto = req.Desconto ?? 0m;
        var saldo = req.ValorTotal - req.ValorEntrada - desconto;
        if (saldo <= 0) throw new DadosInvalidosException("CONTRATO_SALDO_INVALIDO", "Saldo a parcelar precisa ser maior que zero.");

        var valorParcela = Math.Round(saldo / req.NumParcelas, 2);
        var resto = saldo - (valorParcela * req.NumParcelas);

        await using var tx = await db.Database.BeginTransactionAsync();

        var contrato = new Contrato
        {
            AlunoId = req.AlunoId,
            Pacote = req.Pacote,
            ValorTotal = req.ValorTotal,
            ValorEntrada = req.ValorEntrada,
            Desconto = desconto,
            NumParcelas = req.NumParcelas,
            DiaVencimento = req.DiaVencimento,
            AutorizaImagem = req.AutorizaImagem ?? true,
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

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] ContratoUpdate req, AppDbContext db)
    {
        var c = await db.Contratos.Include(x => x.Parcelas).FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) throw new RecursoNaoEncontradoException("Contrato", id);

        c.Pacote = req.Pacote;
        c.ValorTotal = req.ValorTotal;
        c.ValorEntrada = req.ValorEntrada;
        c.Desconto = req.Desconto ?? 0m;
        c.NumParcelas = req.NumParcelas;
        c.DiaVencimento = req.DiaVencimento;
        if (req.AutorizaImagem.HasValue) c.AutorizaImagem = req.AutorizaImagem.Value;
        c.FormaPagamento = req.FormaPagamento;
        if (req.TextoContrato is not null) c.TextoContrato = req.TextoContrato;
        c.AtualizadoEm = DateTimeOffset.UtcNow;

        if (req.RecalcularParcelas == true && req.PrimeiroVencimento.HasValue)
        {
            var saldo = c.ValorTotal - c.ValorEntrada - c.Desconto;
            if (saldo <= 0) throw new DadosInvalidosException("CONTRATO_SALDO_INVALIDO", "Saldo a parcelar precisa ser maior que zero.");

            db.Parcelas.RemoveRange(c.Parcelas);

            var valorParcela = Math.Round(saldo / c.NumParcelas, 2);
            var resto = saldo - (valorParcela * c.NumParcelas);

            for (var i = 1; i <= c.NumParcelas; i++)
            {
                var venc = req.PrimeiroVencimento.Value.AddMonths(i - 1);
                var valor = valorParcela + (i == c.NumParcelas ? resto : 0);
                db.Parcelas.Add(new Parcela
                {
                    ContratoId = c.Id,
                    Numero = i,
                    Valor = valor,
                    Vencimento = venc,
                    Status = StatusParcela.Pendente
                });
            }
        }

        await db.SaveChangesAsync();
        return Results.Ok(c);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var deleted = await db.Contratos.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (deleted == 0) throw new RecursoNaoEncontradoException("Contrato", id);
        return Results.NoContent();
    }
}
