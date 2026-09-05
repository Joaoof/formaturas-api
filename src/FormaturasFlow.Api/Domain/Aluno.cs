using FormaturasFlow.Api.Data;

namespace FormaturasFlow.Api.Domain;

public class Aluno
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TurmaId { get; set; }
    public Turma? Turma { get; set; }

    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string NomeCompleto { get; set; } = string.Empty;
    public string? Cpf { get; set; }
    public string? Rg { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Whatsapp { get; set; }
    public string? Endereco { get; set; }
    public string? Cidade { get; set; }
    public string? Cep { get; set; }
    public string? LoginUsuario { get; set; }

    public string? AsaasCustomerId { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Contrato> Contratos { get; set; } = new List<Contrato>();
}
