using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class ColaboradorEndpoints
{
    public record ColaboradorCreate(
        string Nome, string Funcao, decimal SalarioBase,
        string? Telefone, string? ChavePix, string? Email,
        DateOnly? DataAdmissao, string? Observacoes);

    public record ColaboradorUpdate(
        string Nome, string Funcao, decimal SalarioBase,
        string? Telefone, string? ChavePix, string? Email,
        DateOnly? DataAdmissao, string? Observacoes,
        string? Status);

    public record LancamentoCreate(
        Guid ColaboradorId, string Tipo, string Categoria,
        string Descricao, decimal Valor, DateOnly Data,
        string? ReferenciaMesAno);

    public static IEndpointRouteBuilder MapColaboradorEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/colaboradores").WithTags("Colaboradores").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, [FromQuery] string? status) =>
        {
            var q = db.Colaboradores.AsNoTracking();
            if (Enum.TryParse<StatusColaborador>(status, ignoreCase: true, out var s))
                q = q.Where(c => c.Status == s);
            return await q.OrderBy(c => c.Nome).ToListAsync();
        })
            .WithSummary("Lista colaboradores")
            .Produces<Colaborador[]>(StatusCodes.Status200OK);

        g.MapGet("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var c = await db.Colaboradores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) throw new RecursoNaoEncontradoException("Colaborador", id);
            return Results.Ok(c);
        })
            .WithSummary("Detalhe do colaborador")
            .Produces<Colaborador>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        g.MapPost("/", async (ColaboradorCreate req, AppDbContext db) =>
        {
            var c = new Colaborador
            {
                Nome = req.Nome, Funcao = req.Funcao, SalarioBase = req.SalarioBase,
                Telefone = req.Telefone, ChavePix = req.ChavePix, Email = req.Email,
                DataAdmissao = req.DataAdmissao, Observacoes = req.Observacoes
            };
            db.Colaboradores.Add(c);
            await db.SaveChangesAsync();
            return Results.Created($"/colaboradores/{c.Id}", c);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria um colaborador")
            .Produces<Colaborador>(StatusCodes.Status201Created);

        g.MapPut("/{id:guid}", async (Guid id, ColaboradorUpdate req, AppDbContext db) =>
        {
            var c = await db.Colaboradores.FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) throw new RecursoNaoEncontradoException("Colaborador", id);

            c.Nome = req.Nome; c.Funcao = req.Funcao; c.SalarioBase = req.SalarioBase;
            c.Telefone = req.Telefone; c.ChavePix = req.ChavePix; c.Email = req.Email;
            c.DataAdmissao = req.DataAdmissao; c.Observacoes = req.Observacoes;
            if (Enum.TryParse<StatusColaborador>(req.Status, ignoreCase: true, out var s))
                c.Status = s;
            c.AtualizadoEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(c);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Atualiza colaborador")
            .Produces<Colaborador>(StatusCodes.Status200OK);

        g.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            /*  Cascata pela FK apaga tambem os lancamentos vinculados. */
            var deleted = await db.Colaboradores.Where(x => x.Id == id).ExecuteDeleteAsync();
            if (deleted == 0) throw new RecursoNaoEncontradoException("Colaborador", id);
            return Results.NoContent();
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin))
            .WithSummary("Remove colaborador e seus lancamentos")
            .Produces(StatusCodes.Status204NoContent);

        var l = app.MapGroup("/lancamentos-colaboradores").WithTags("Colaboradores").RequireAuthorization();

        l.MapGet("/", async (AppDbContext db, [FromQuery] Guid? colaboradorId, [FromQuery] string? mesAno) =>
        {
            var q = db.LancamentosColaboradores.AsNoTracking();
            if (colaboradorId.HasValue) q = q.Where(x => x.ColaboradorId == colaboradorId.Value);
            if (!string.IsNullOrWhiteSpace(mesAno))
                q = q.Where(x => x.ReferenciaMesAno == mesAno);
            return await q.OrderByDescending(x => x.Data).ToListAsync();
        })
            .WithSummary("Lista lancamentos (filtra por colaboradorId e mesAno YYYY-MM)")
            .Produces<LancamentoColaborador[]>(StatusCodes.Status200OK);

        l.MapPost("/", async (LancamentoCreate req, AppDbContext db) =>
        {
            var colabExiste = await db.Colaboradores.AnyAsync(c => c.Id == req.ColaboradorId);
            if (!colabExiste) throw new RecursoNaoEncontradoException("Colaborador", req.ColaboradorId);

            if (!Enum.TryParse<TipoLancamento>(req.Tipo, ignoreCase: true, out var tipo))
                throw new DadosInvalidosException(
                    "LANCAMENTO_TIPO_INVALIDO",
                    "Tipo invalido. Use 'entrada' ou 'saida'.");

            var lanc = new LancamentoColaborador
            {
                ColaboradorId = req.ColaboradorId,
                Tipo = tipo,
                Categoria = req.Categoria,
                Descricao = req.Descricao,
                Valor = req.Valor,
                Data = req.Data,
                ReferenciaMesAno = req.ReferenciaMesAno ?? req.Data.ToString("yyyy-MM")
            };
            db.LancamentosColaboradores.Add(lanc);
            await db.SaveChangesAsync();
            return Results.Created($"/lancamentos-colaboradores/{lanc.Id}", lanc);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria um lancamento (entrada/saida) de colaborador")
            .Produces<LancamentoColaborador>(StatusCodes.Status201Created);

        l.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var deleted = await db.LancamentosColaboradores.Where(x => x.Id == id).ExecuteDeleteAsync();
            if (deleted == 0) throw new RecursoNaoEncontradoException("Lancamento", id);
            return Results.NoContent();
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Remove lancamento")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
