namespace FormaturasFlow.Api.Domain;

/*  Base de toda violação de regra de negócio.

    `Codigo` é o contrato estável com o front-end: a UI decide o que
    renderizar a partir dele, nunca a partir de `Message` (que é texto
    em português, sujeito a mudança).  `Detalhes` carrega os dados que
    a tela precisa para se recuperar sozinha — por exemplo, a lista de
    métodos de pagamento que o domínio realmente aceita.

    `StatusCode` permite subclasses expressarem melhor o erro semanticamente
    (404 para "não encontrado", 409 para conflito, etc.) sem sair do fluxo
    de exceção. Default é 422 porque a maior parte das regras de negócio
    trata dados que passaram no schema mas são inválidos no domínio.  */
public abstract class DomainException(string codigo, string mensagem, int statusCode = StatusCodes.Status422UnprocessableEntity)
    : Exception(mensagem)
{
    public string Codigo { get; } = codigo;

    public int StatusCode { get; } = statusCode;

    public Dictionary<string, object?> Detalhes { get; } = [];
}

/*  Recurso solicitado não existe. Substitui `Results.NotFound(...)` avulsos
    e mantém o contrato de erro consistente (mesmo shape de ProblemDetails).  */
public sealed class RecursoNaoEncontradoException(string recurso, object id)
    : DomainException(
        codigo:     $"{recurso.ToUpperInvariant()}_NAO_ENCONTRADO",
        mensagem:   $"{recurso} '{id}' não encontrado.",
        statusCode: StatusCodes.Status404NotFound)
{ }

/*  Dados válidos por schema mas inconsistentes no domínio — CPF fora do
    formato, número de parcelas negativo, saldo insuficiente, etc. */
public sealed class DadosInvalidosException(string codigo, string mensagem)
    : DomainException(codigo, mensagem, StatusCodes.Status400BadRequest)
{ }

/*  Estado atual do recurso impede a operação — parcela já paga, aluno
    inativo, credencial expirada. Semanticamente diferente de "inválido":
    o dado está certo, mas o momento é errado.  */
public sealed class ConflitoException(string codigo, string mensagem)
    : DomainException(codigo, mensagem, StatusCodes.Status409Conflict)
{ }

/*  Usuário autenticado mas sem permissão para o recurso solicitado. Fica
    fora do 401, que é responsabilidade do middleware de auth. */
public sealed class AcessoNegadoException(string codigo, string mensagem)
    : DomainException(codigo, mensagem, StatusCodes.Status403Forbidden)
{ }
