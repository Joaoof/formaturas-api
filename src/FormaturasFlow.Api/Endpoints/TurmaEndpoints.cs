using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class TurmaEndpoints
{
    public record TurmaDto(
        Guid Id,
        string Nome,
        string? Faculdade,
        string? Instituicao,
        string? Curso,
        string? Cidade,
        string? Semestre,
        int? AnoFormatura,
        DateOnly? PrevisaoFormatura,
        TipoEvento TipoEvento,
        DateOnly? DataEvento,
        StatusTurma Status,
        int TotalAlunos);

    public record TurmaCreate(
        string Nome,
        string? Faculdade,
        string? Instituicao,
        string? Curso,
        string? Cidade,
        string? Semestre,
        int? AnoFormatura,
        DateOnly? PrevisaoFormatura,
        TipoEvento? TipoEvento,
        DateOnly? DataEvento,
        StatusTurma? Status,
        string? Observacoes);

    public record TurmaUpdate(
        string Nome,
        string? Faculdade,
        string? Instituicao,
        string? Curso,
        string? Cidade,
        string? Semestre,
        int? AnoFormatura,
        DateOnly? PrevisaoFormatura,
        TipoEvento? TipoEvento,
        DateOnly? DataEvento,
        StatusTurma? Status,
        string? Observacoes);

    public static IEndpointRouteBuilder MapTurmaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/turmas").WithTags("Turmas").RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithSummary("Lista todas as turmas")
            .WithDescription("Cada item traz também `totalAlunos`.")
            .Produces<TurmaDto[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Detalhe de uma turma")
            .Produces<Turma>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria uma nova turma")
            .WithDescription("Requer papel `super_admin` ou `funcionario`.")
            .Produces<Turma>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Atualiza uma turma existente")
            .Produces<Turma>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin))
            .WithSummary("Remove uma turma")
            .WithDescription("Cascata: apaga também os alunos vinculados. Só `super_admin`.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db)
    {
        var list = await db.Turmas
            .AsNoTracking()
            .OrderByDescending(t => t.CriadaEm)
            .Select(t => new TurmaDto(
                t.Id, t.Nome, t.Faculdade, t.Instituicao, t.Curso, t.Cidade, t.Semestre,
                t.AnoFormatura, t.PrevisaoFormatura, t.TipoEvento, t.DataEvento, t.Status,
                t.Alunos.Count))
            .ToListAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db)
    {
        var t = await db.Turmas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) throw new RecursoNaoEncontradoException("Turma", id);
        return Results.Ok(t);
    }

    private static async Task<IResult> CreateAsync([FromBody] TurmaCreate req, AppDbContext db)
    {
        var t = new Turma
        {
            Nome = req.Nome,
            Faculdade = req.Faculdade ?? req.Instituicao,
            Instituicao = req.Instituicao ?? req.Faculdade,
            Curso = req.Curso,
            Cidade = req.Cidade,
            Semestre = req.Semestre,
            AnoFormatura = req.AnoFormatura,
            PrevisaoFormatura = req.PrevisaoFormatura,
            TipoEvento = req.TipoEvento ?? TipoEvento.Formatura,
            DataEvento = req.DataEvento,
            Status = req.Status ?? StatusTurma.Ativa,
            Observacoes = req.Observacoes
        };
        db.Turmas.Add(t);
        await db.SaveChangesAsync();
        return Results.Created($"/turmas/{t.Id}", t);
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] TurmaUpdate req, AppDbContext db)
    {
        var t = await db.Turmas.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) throw new RecursoNaoEncontradoException("Turma", id);

        t.Nome = req.Nome;
        t.Faculdade = req.Faculdade ?? req.Instituicao ?? t.Faculdade;
        t.Instituicao = req.Instituicao ?? req.Faculdade ?? t.Instituicao;
        t.Curso = req.Curso;
        t.Cidade = req.Cidade;
        t.Semestre = req.Semestre;
        t.AnoFormatura = req.AnoFormatura;
        t.PrevisaoFormatura = req.PrevisaoFormatura;
        if (req.TipoEvento.HasValue) t.TipoEvento = req.TipoEvento.Value;
        t.DataEvento = req.DataEvento;
        if (req.Status.HasValue) t.Status = req.Status.Value;
        t.Observacoes = req.Observacoes;
        t.AtualizadaEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(t);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var deleted = await db.Turmas.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (deleted == 0) throw new RecursoNaoEncontradoException("Turma", id);
        return Results.NoContent();
    }
}
