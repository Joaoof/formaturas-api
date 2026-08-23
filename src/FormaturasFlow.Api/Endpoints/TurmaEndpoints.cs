using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class TurmaEndpoints
{
    public record TurmaDto(Guid Id, string Nome, string? Instituicao, string? Curso, int? AnoFormatura, int TotalAlunos);
    public record TurmaCreate(string Nome, string? Instituicao, string? Curso, int? AnoFormatura, string? Observacoes);
    public record TurmaUpdate(string Nome, string? Instituicao, string? Curso, int? AnoFormatura, string? Observacoes);

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
            .Select(t => new TurmaDto(t.Id, t.Nome, t.Instituicao, t.Curso, t.AnoFormatura, t.Alunos.Count))
            .ToListAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db)
    {
        var t = await db.Turmas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return t is null ? Results.NotFound() : Results.Ok(t);
    }

    private static async Task<IResult> CreateAsync([FromBody] TurmaCreate req, AppDbContext db)
    {
        var t = new Turma
        {
            Nome = req.Nome,
            Instituicao = req.Instituicao,
            Curso = req.Curso,
            AnoFormatura = req.AnoFormatura,
            Observacoes = req.Observacoes
        };
        db.Turmas.Add(t);
        await db.SaveChangesAsync();
        return Results.Created($"/turmas/{t.Id}", t);
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] TurmaUpdate req, AppDbContext db)
    {
        var t = await db.Turmas.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return Results.NotFound();

        t.Nome = req.Nome;
        t.Instituicao = req.Instituicao;
        t.Curso = req.Curso;
        t.AnoFormatura = req.AnoFormatura;
        t.Observacoes = req.Observacoes;
        t.AtualizadaEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(t);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var deleted = await db.Turmas.Where(x => x.Id == id).ExecuteDeleteAsync();
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }
}
