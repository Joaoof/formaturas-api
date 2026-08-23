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

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync).RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario));
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario));
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin));

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
