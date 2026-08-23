using Microsoft.AspNetCore.Identity;

namespace FormaturasFlow.Api.Data;

public class ApplicationUser : IdentityUser<Guid>
{
    public string NomeCompleto { get; set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { }
}

public static class Roles
{
    public const string SuperAdmin = "super_admin";
    public const string Funcionario = "funcionario";
    public const string Aluno = "aluno";
}
