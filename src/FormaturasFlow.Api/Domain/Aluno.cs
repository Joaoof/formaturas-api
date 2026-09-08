using FormaturasFlow.Api.Data;

namespace FormaturasFlow.Api.Domain;

public enum StatusAluno { Ativo = 0, Inativo = 1 }

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
    public DateOnly? DataNascimento { get; set; }

    public StatusAluno Status { get; set; } = StatusAluno.Ativo;
    public string? MotivoInativacao { get; set; }

    public string? LinkFotosSelecionadas { get; set; }
    public int? PrazoFotosSelecionadas { get; set; }
    public DateOnly? VencimentoFotosSelecionadas { get; set; }
    public bool FotosLiberadas { get; set; }
    public string? LinkAprovacaoAlbum { get; set; }
    public bool AlbumLiberado { get; set; }

    public string? AsaasCustomerId { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Contrato> Contratos { get; set; } = new List<Contrato>();
}
