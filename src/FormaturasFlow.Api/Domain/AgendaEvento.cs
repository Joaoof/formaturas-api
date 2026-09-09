namespace FormaturasFlow.Api.Domain;

public class AgendaEvento
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titulo { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public string EmpresaTipo { get; set; } = "jm";
    public string EmpresaNome { get; set; } = "JM Formaturas & Eventos";
    public string? LocalEvento { get; set; }
    public string? Cidade { get; set; }
    public string? Fotografo { get; set; }
    public DateOnly DataEvento { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}
