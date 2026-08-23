namespace FormaturasFlow.Api.Domain;

public class Contrato
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AlunoId { get; set; }
    public Aluno? Aluno { get; set; }

    public string? Pacote { get; set; }
    public decimal ValorTotal { get; set; }
    public decimal ValorEntrada { get; set; }
    public int NumParcelas { get; set; } = 1;
    public string? FormaPagamento { get; set; }
    public DateOnly DataContrato { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string? TextoContrato { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Parcela> Parcelas { get; set; } = new List<Parcela>();
}
