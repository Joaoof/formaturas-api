namespace FormaturasFlow.Api.Auth;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevogadoEm { get; set; }
    public string? RevogadoMotivo { get; set; }
    public Guid? SubstituidoPorId { get; set; }
    public string? IpCriacao { get; set; }
    public string? UserAgent { get; set; }

    public bool Ativo => RevogadoEm is null && DateTimeOffset.UtcNow < ExpiresAt;
}

public class ServiceToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public Guid CriadoPorUserId { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UltimoUsoEm { get; set; }
    public DateTimeOffset? RevogadoEm { get; set; }
    public string? Escopo { get; set; }

    public bool Ativo => RevogadoEm is null && DateTimeOffset.UtcNow < ExpiresAt;
}
