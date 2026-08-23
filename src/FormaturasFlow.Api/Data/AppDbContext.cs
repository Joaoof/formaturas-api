using FormaturasFlow.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<Turma> Turmas => Set<Turma>();
    public DbSet<Aluno> Alunos => Set<Aluno>();
    public DbSet<Contrato> Contratos => Set<Contrato>();
    public DbSet<Parcela> Parcelas => Set<Parcela>();
    public DbSet<Despesa> Despesas => Set<Despesa>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Turma>(e =>
        {
            e.ToTable("turmas");
            e.HasIndex(x => x.Nome);
        });

        b.Entity<Aluno>(e =>
        {
            e.ToTable("alunos");
            e.HasIndex(x => x.TurmaId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.LoginUsuario).IsUnique();
            e.HasOne(x => x.Turma).WithMany(t => t.Alunos)
                .HasForeignKey(x => x.TurmaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Contrato>(e =>
        {
            e.ToTable("contratos");
            e.HasIndex(x => x.AlunoId);
            e.Property(x => x.ValorTotal).HasPrecision(12, 2);
            e.Property(x => x.ValorEntrada).HasPrecision(12, 2);
            e.HasOne(x => x.Aluno).WithMany(a => a.Contratos)
                .HasForeignKey(x => x.AlunoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Parcela>(e =>
        {
            e.ToTable("parcelas");
            e.HasIndex(x => x.ContratoId);
            e.HasIndex(x => new { x.ContratoId, x.Numero }).IsUnique();
            e.HasIndex(x => x.PspChargeId);
            e.Property(x => x.Valor).HasPrecision(12, 2);
            e.Property(x => x.ValorPago).HasPrecision(12, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasOne(x => x.Contrato).WithMany(c => c.Parcelas)
                .HasForeignKey(x => x.ContratoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Despesa>(e =>
        {
            e.ToTable("despesas");
            e.Property(x => x.Valor).HasPrecision(12, 2);
            e.Property(x => x.Status).HasConversion<string>();
        });

        b.Entity<WebhookEvent>(e =>
        {
            e.ToTable("webhook_events");
            e.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        });
    }
}
