using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FormaturasFlow.Api.Endpoints;

/*  Traduz DomainException em ProblemDetails (RFC 9457) com 422.

    O contrato com o front é `codigo` + as chaves de `Detalhes`: a UI faz
    switch no código, nunca em string de mensagem nem em status HTTP.  */
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<DomainExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not DomainException dex) return false;

        log.LogWarning("Regra de negócio violada: {Codigo} — {Mensagem}", dex.Codigo, dex.Message);

        ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title  = "Regra de negócio violada",
            Detail = dex.Message,
            Type   = $"https://api.formaturasflow.com.br/errors/{dex.Codigo.ToLowerInvariant()}"
        };

        problema.Extensions["codigo"] = dex.Codigo;
        foreach (var (chave, valor) in dex.Detalhes)
            problema.Extensions[chave] = valor;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext    = ctx,
            Exception      = ex,
            ProblemDetails = problema
        });
    }
}
