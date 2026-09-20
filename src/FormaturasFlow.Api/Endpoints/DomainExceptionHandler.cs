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

        log.LogWarning("Regra de negócio violada: {Codigo} ({Status}) — {Mensagem}",
            dex.Codigo, dex.StatusCode, dex.Message);

        ctx.Response.StatusCode = dex.StatusCode;

        var titulo = dex.StatusCode switch
        {
            StatusCodes.Status404NotFound          => "Recurso não encontrado",
            StatusCodes.Status400BadRequest        => "Dados inválidos",
            StatusCodes.Status409Conflict          => "Conflito no estado do recurso",
            StatusCodes.Status403Forbidden         => "Acesso negado",
            _                                      => "Regra de negócio violada"
        };

        var problema = new ProblemDetails
        {
            Status = dex.StatusCode,
            Title  = titulo,
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
