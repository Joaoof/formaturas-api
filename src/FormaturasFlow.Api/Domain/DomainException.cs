namespace FormaturasFlow.Api.Domain;

/*  Base de toda violação de regra de negócio.

    `Codigo` é o contrato estável com o front-end: a UI decide o que
    renderizar a partir dele, nunca a partir de `Message` (que é texto
    em português, sujeito a mudança).  `Detalhes` carrega os dados que
    a tela precisa para se recuperar sozinha — por exemplo, a lista de
    métodos de pagamento que o domínio realmente aceita.  */
public abstract class DomainException(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;

    public Dictionary<string, object?> Detalhes { get; } = [];
}
