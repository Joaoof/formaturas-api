namespace FormaturasFlow.Api.Domain;

public class Turma
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public string? Instituicao { get; set; }
    public string? Curso { get; set; }
    public int? AnoFormatura { get; set; }
    public string? Observacoes { get; set; }
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadaEm { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Aluno> Alunos { get; set; } = new List<Aluno>();
}
