using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Pagamentos;

public static class CobrancaEndpoints
{
    public record CriarRequest(
        string ExternalReference,
        string ClienteNome,
        string? ClienteCpf,
        string? ClienteEmail,
        string? ClienteWhatsapp,
        string? ClienteTelefone,
        decimal Valor,
        DateOnly Vencimento,
        string Descricao,
        string Tipo,
        int? NumParcelasCartao,
        TipoEvento? TipoEvento);

    public static IEndpointRouteBuilder MapCobrancaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cobrancas").WithTags("Cobrancas");

        group.MapPost("/", CriarAsync)
            .RequireAuthorization(p => p.RequireRole(Roles.SuperAdmin, Roles.Funcionario))
            .WithSummary("Cria uma cobranca standalone (sem depender de parcela existente)")
            .WithDescription("""
                Recebe todos os dados do cliente + valor + tipo, chama o PSP (Asaas ou Cora)
                e retorna o link de pagamento. Se o webhook confirmar depois, o status da
                Cobranca vira Confirmado automaticamente.

                Body:
                ```json
                {
                  "externalReference": "uuid-da-parcela-Supabase-ou-qualquer-id-do-cliente",
                  "clienteNome": "Maria Silva",
                  "clienteCpf": "12345678900",
                  "clienteEmail": "maria@ex.com",
                  "clienteWhatsapp": "11987654321",
                  "valor": 300.00,
                  "vencimento": "2026-10-05",
                  "descricao": "Parcela 3 - Casamento Ana e Bruno",
                  "tipo": "cartao" | "pix" | "boleto" | "checkout",
                  "numParcelasCartao": 3,
                  "tipoEvento": "Casamento" | "Formatura" | "Outro"
                }
                ```
                """)
            .Produces<Cobranca>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/by-ref/{externalRef}", GetByRefAsync)
            .RequireAuthorization()
            .WithSummary("Consulta cobranca por externalReference")
            .Produces<Cobranca>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization()
            .WithSummary("Consulta cobranca por id")
            .Produces<Cobranca>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CriarAsync(
        [FromBody] CriarRequest req,
        AppDbContext db,
        PagamentoService service,
        CancellationToken ct)
    {
        if (!Enum.TryParse<TipoPagamento>(req.Tipo, ignoreCase: true, out var tipo))
            return Results.BadRequest(new { erro = "tipo invalido. Use 'pix', 'boleto', 'cartao' ou 'checkout'." });

        var existente = await db.Cobrancas
            .FirstOrDefaultAsync(x => x.ExternalReference == req.ExternalReference, ct);
        if (existente is not null && !string.IsNullOrEmpty(existente.PspChargeId))
            return Results.Ok(new { existente = true, cobranca = existente });

        var cobranca = new Cobranca
        {
            ExternalReference = req.ExternalReference,
            ClienteNome = req.ClienteNome,
            ClienteCpf = req.ClienteCpf,
            ClienteEmail = req.ClienteEmail,
            ClienteWhatsapp = req.ClienteWhatsapp,
            ClienteTelefone = req.ClienteTelefone,
            Valor = req.Valor,
            Vencimento = req.Vencimento,
            Descricao = req.Descricao,
            TipoEvento = req.TipoEvento ?? TipoEvento.Formatura
        };

        var criada = await service.EmitirCobrancaStandaloneAsync(cobranca, tipo, req.NumParcelasCartao, ct);
        return Results.Created($"/api/v1/cobrancas/{criada.Id}", criada);
    }

    private static async Task<IResult> GetByRefAsync(string externalRef, AppDbContext db, CancellationToken ct)
    {
        var c = await db.Cobrancas.AsNoTracking().FirstOrDefaultAsync(x => x.ExternalReference == externalRef, ct);
        return c is null ? Results.NotFound() : Results.Ok(c);
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var c = await db.Cobrancas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? Results.NotFound() : Results.Ok(c);
    }
}
