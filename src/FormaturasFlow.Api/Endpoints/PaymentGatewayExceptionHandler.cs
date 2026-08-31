using FormaturasFlow.Api.Payments;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FormaturasFlow.Api.Endpoints;

/*  Falha do PSP vira 502, não 500: a distinção importa para o front, que
    oferece "tentar novamente" em vez de pedir correção de formulário —
    e para o alerta, porque 502 aqui é indisponibilidade de terceiro.  */
public sealed class PaymentGatewayExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<PaymentGatewayExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not PaymentGatewayException gex) return false;

        log.LogError(gex, "Falha no PSP {Provider}", gex.Provider);

        ctx.Response.StatusCode = StatusCodes.Status502BadGateway;

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title  = "Provedor de pagamento indisponível",
            Detail = gex.Message,
            Type   = "https://api.formaturasflow.com.br/errors/gateway_indisponivel"
        };

        problema.Extensions["codigo"]   = "GATEWAY_INDISPONIVEL";
        problema.Extensions["provider"] = gex.Provider.ToString();

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext    = ctx,
            Exception      = ex,
            ProblemDetails = problema
        });
    }
}
