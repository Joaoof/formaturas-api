using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class AlunoEndpoints
{
    public record AlunoDto(
        Guid Id, Guid TurmaId, string NomeCompleto, string? Cpf, string? Email,
        string? Whatsapp, string? Telefone, string Status, string? MotivoInativacao,
        DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm);

    public record AlunoCreate(
        Guid TurmaId, string NomeCompleto, string? Cpf, string? Rg,
        string? Email, string? Telefone, string? Whatsapp,
        string? Endereco, string? Cidade, string? Cep,
        DateOnly? DataNascimento);

    public record AlunoUpdate(
        string NomeCompleto, string? Cpf, string? Rg,
        string? Email, string? Telefone, string? Whatsapp,
        string? Endereco, string? Cidade, string? Cep,
        DateOnly? DataNascimento);

    public record AlunoInativar(string? Motivo);

    public record AlunoLinks(
        string? LinkFotosSelecionadas,
        int? PrazoFotosSelecionadas,
        DateOnly? VencimentoFotosSelecionadas,
        bool? FotosLiberadas,
        string? LinkAprovacaoAlbum,
        bool? AlbumLiberado);

    public static IEndpointRouteBuilder MapAlunoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/alunos").WithTags("Alunos").RequireAuthorization();

        group.MapGet("/me", ListMineAsync)
            .WithSummary("Lista alunos vinculados ao usuario logado (por UserId ou CPF do e-mail cpf@formandos.local)")
            .Produces<Aluno[]>(StatusCodes.Status200OK);

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

        group.MapPost("/{id:guid}/inativar", InativarAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Inativa um aluno e cancela parcelas nao pagas")
            .Produces<Aluno>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reativar", ReativarAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Reativa um aluno inativo")
            .Produces<Aluno>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/links", UpdateLinksAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Atualiza links de fotos selecionadas e aprovacao de album")
            .Produces<Aluno>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListMineAsync(AppDbContext db, HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? ctx.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId))
            return Results.Unauthorized();

        var emailClaim = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                         ?? ctx.User.FindFirst("email")?.Value;
        string? cpfDoEmail = null;
        if (!string.IsNullOrWhiteSpace(emailClaim))
        {
            var localPart = emailClaim.Split('@')[0];
            var digits = new string(localPart.Where(char.IsDigit).ToArray());
            if (digits.Length == 11) cpfDoEmail = digits;
        }

        var q = db.Alunos.Include(a => a.Turma)
            .Include(a => a.Contratos).ThenInclude(c => c.Parcelas)
            .AsQueryable();

        if (cpfDoEmail is null)
            q = q.Where(a => a.UserId == userId);
        else
            q = q.Where(a => a.UserId == userId || a.Cpf == cpfDoEmail);

        var alunos = await q.AsNoTracking().ToListAsync();
        return Results.Ok(alunos);
    }

    private static async Task<IResult> ListAsync(AppDbContext db, [FromQuery] Guid? turmaId = null, [FromQuery] string? status = null)
    {
        var q = db.Alunos.AsNoTracking();
        if (turmaId.HasValue) q = q.Where(a => a.TurmaId == turmaId.Value);
        if (Enum.TryParse<StatusAluno>(status, ignoreCase: true, out var s))
            q = q.Where(a => a.Status == s);
        var list = await q
            .OrderBy(a => a.NomeCompleto)
            .Select(a => new AlunoDto(a.Id, a.TurmaId, a.NomeCompleto, a.Cpf, a.Email, a.Whatsapp, a.Telefone, a.Status.ToString(), a.MotivoInativacao, a.CriadoEm, a.AtualizadoEm))
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
            Cep = req.Cep,
            DataNascimento = req.DataNascimento
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
        a.DataNascimento = req.DataNascimento;
        a.AtualizadoEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(a);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var deleted = await db.Alunos.Where(x => x.Id == id).ExecuteDeleteAsync();
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> InativarAsync(Guid id, [FromBody] AlunoInativar req, AppDbContext db)
    {
        var a = await db.Alunos.Include(x => x.Contratos).ThenInclude(c => c.Parcelas)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return Results.NotFound();

        a.Status = StatusAluno.Inativo;
        a.MotivoInativacao = req.Motivo;
        a.AtualizadoEm = DateTimeOffset.UtcNow;

        foreach (var c in a.Contratos)
        {
            var paraCancelar = c.Parcelas.Where(p => p.Status != StatusParcela.Pago).ToList();
            db.Parcelas.RemoveRange(paraCancelar);
        }

        await db.SaveChangesAsync();
        return Results.Ok(a);
    }

    private static async Task<IResult> ReativarAsync(Guid id, AppDbContext db)
    {
        var a = await db.Alunos.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return Results.NotFound();

        a.Status = StatusAluno.Ativo;
        a.MotivoInativacao = null;
        a.AtualizadoEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(a);
    }

    private static async Task<IResult> UpdateLinksAsync(Guid id, [FromBody] AlunoLinks req, AppDbContext db)
    {
        var a = await db.Alunos.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return Results.NotFound();

        if (req.LinkFotosSelecionadas is not null) a.LinkFotosSelecionadas = req.LinkFotosSelecionadas;
        if (req.PrazoFotosSelecionadas.HasValue)
        {
            a.PrazoFotosSelecionadas = req.PrazoFotosSelecionadas;
            a.VencimentoFotosSelecionadas = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(req.PrazoFotosSelecionadas.Value);
        }
        if (req.VencimentoFotosSelecionadas.HasValue)
            a.VencimentoFotosSelecionadas = req.VencimentoFotosSelecionadas;
        if (req.FotosLiberadas.HasValue) a.FotosLiberadas = req.FotosLiberadas.Value;
        if (req.LinkAprovacaoAlbum is not null) a.LinkAprovacaoAlbum = req.LinkAprovacaoAlbum;
        if (req.AlbumLiberado.HasValue) a.AlbumLiberado = req.AlbumLiberado.Value;

        a.AtualizadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(a);
    }
}
