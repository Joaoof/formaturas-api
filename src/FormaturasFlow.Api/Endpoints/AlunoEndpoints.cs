using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class AlunoEndpoints
{
    public record AlunoDto(
        Guid Id, Guid TurmaId, string NomeCompleto, string? Cpf, string? Email,
        string? Whatsapp, string? Telefone, DateTimeOffset CriadoEm);

    public record AlunoCreate(
        Guid TurmaId, string NomeCompleto, string? Cpf, string? Rg,
        string? Email, string? Telefone, string? Whatsapp,
        string? Endereco, string? Cidade, string? Cep);

    public record AlunoUpdate(
        string NomeCompleto, string? Cpf, string? Rg,
        string? Email, string? Telefone, string? Whatsapp,
        string? Endereco, string? Cidade, string? Cep);

    public static IEndpointRouteBuilder MapAlunoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/alunos").WithTags("Alunos").RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithSummary("Lista alunos")
            .WithDescription("Aceita filtro opcional `?turmaId=<guid>` para pegar apenas alunos de uma turma.")
            .Produces<AlunoDto[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Detalhe de um aluno")
            .WithDescription("Inclui contratos e parcelas do aluno.")
            .Produces<Aluno>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria um novo aluno em uma turma")
            .WithDescription("A turma precisa existir. Requer papel `super_admin` ou `funcionario`.")
            .Produces<Aluno>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Atualiza dados de um aluno")
            .Produces<Aluno>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin))
            .WithSummary("Remove um aluno")
            .WithDescription("Cascata: apaga também contratos e parcelas do aluno. Só `super_admin`.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, [FromQuery] Guid? turmaId = null)
    {
        var q = db.Alunos.AsNoTracking();
        if (turmaId.HasValue) q = q.Where(a => a.TurmaId == turmaId.Value);
        var list = await q
            .Select(a => new AlunoDto(a.Id, a.TurmaId, a.NomeCompleto, a.Cpf, a.Email, a.Whatsapp, a.Telefone, a.CriadoEm))
            .ToListAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db)
    {
        var a = await db.Alunos
            .Include(x => x.Contratos).ThenInclude(c => c.Parcelas)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        return a is null ? Results.NotFound() : Results.Ok(a);
    }

    private static async Task<IResult> CreateAsync([FromBody] AlunoCreate req, AppDbContext db)
    {
        var turmaExiste = await db.Turmas.AnyAsync(t => t.Id == req.TurmaId);
        if (!turmaExiste) return Results.NotFound(new { erro = "Turma não encontrada." });

        var a = new Aluno
        {
            TurmaId = req.TurmaId,
            NomeCompleto = req.NomeCompleto,
            Cpf = req.Cpf,
            Rg = req.Rg,
            Email = req.Email,
            Telefone = req.Telefone,
            Whatsapp = req.Whatsapp,
            Endereco = req.Endereco,
            Cidade = req.Cidade,
            Cep = req.Cep
        };
        db.Alunos.Add(a);
        await db.SaveChangesAsync();
        return Results.Created($"/alunos/{a.Id}", a);
    }

    private static async Task<IResult> UpdateAsync(Guid id, [FromBody] AlunoUpdate req, AppDbContext db)
    {
        var a = await db.Alunos.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return Results.NotFound();

        a.NomeCompleto = req.NomeCompleto;
        a.Cpf = req.Cpf; a.Rg = req.Rg;
        a.Email = req.Email; a.Telefone = req.Telefone; a.Whatsapp = req.Whatsapp;
        a.Endereco = req.Endereco; a.Cidade = req.Cidade; a.Cep = req.Cep;
        a.AtualizadoEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(a);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var deleted = await db.Alunos.Where(x => x.Id == id).ExecuteDeleteAsync();
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }
}
