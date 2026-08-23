namespace FormaturasFlow.Api.Domain;

public enum StatusDespesa
{
    Pendente = 0,
    Pago = 1
}

public class Despesa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Descricao { get; set; } = string.Empty;
    public string Categoria { get; set; } = "geral";
    public decimal Valor { get; set; }
    public DateOnly Vencimento { get; set; }
    public DateOnly? DataPagamento { get; set; }
    public StatusDespesa Status { get; set; } = StatusDespesa.Pendente;

    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadaEm { get; set; } = DateTimeOffset.UtcNow;
}
