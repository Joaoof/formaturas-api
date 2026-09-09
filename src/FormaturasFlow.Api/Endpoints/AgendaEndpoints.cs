using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class AgendaEndpoints
{
    public record AgendaCreate(
        string Titulo, string? Descricao, string EmpresaTipo, string EmpresaNome,
        string? LocalEvento, string? Cidade, string? Fotografo, DateOnly DataEvento);

    public record AgendaUpdate(
        string Titulo, string? Descricao, string EmpresaTipo, string EmpresaNome,
        string? LocalEvento, string? Cidade, string? Fotografo, DateOnly DataEvento);

    public static IEndpointRouteBuilder MapAgendaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agenda").WithTags("Agenda").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db) =>
            await db.AgendaEventos.AsNoTracking().OrderBy(a => a.DataEvento).ToListAsync())
            .WithSummary("Lista eventos da agenda ordenados por data")
            .Produces<AgendaEvento[]>(StatusCodes.Status200OK);

        group.MapPost("/", async (AgendaCreate req, AppDbContext db) =>
        {
            var e = new AgendaEvento
            {
                Titulo = req.Titulo,
                Descricao = req.Descricao,
                EmpresaTipo = req.EmpresaTipo,
                EmpresaNome = req.EmpresaNome,
                LocalEvento = req.LocalEvento,
                Cidade = req.Cidade,
                Fotografo = req.Fotografo,
                DataEvento = req.DataEvento
            };
            db.AgendaEventos.Add(e);
            await db.SaveChangesAsync();
            return Results.Created($"/agenda/{e.Id}", e);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria um evento na agenda")
            .Produces<AgendaEvento>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async (Guid id, AgendaUpdate req, AppDbContext db) =>
        {
            var e = await db.AgendaEventos.FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return Results.NotFound();
            e.Titulo = req.Titulo;
            e.Descricao = req.Descricao;
            e.EmpresaTipo = req.EmpresaTipo;
            e.EmpresaNome = req.EmpresaNome;
            e.LocalEvento = req.LocalEvento;
            e.Cidade = req.Cidade;
            e.Fotografo = req.Fotografo;
            e.DataEvento = req.DataEvento;
            e.AtualizadoEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(e);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Atualiza um evento da agenda")
            .Produces<AgendaEvento>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var deleted = await db.AgendaEventos.Where(x => x.Id == id).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        })
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Remove um evento da agenda")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
