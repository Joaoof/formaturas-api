namespace FormaturasFlow.Api.Domain;

public enum StatusColaborador { Ativo = 0, Inativo = 1 }

public class Colaborador
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = string.Empty;
    public string Funcao { get; set; } = string.Empty;
    public decimal SalarioBase { get; set; }
    public string? Telefone { get; set; }
    public string? ChavePix { get; set; }
    public StatusColaborador Status { get; set; } = StatusColaborador.Ativo;
    public DateOnly? DataAdmissao { get; set; }
    public string? Email { get; set; }
    public string? Observacoes { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<LancamentoColaborador> Lancamentos { get; set; } = new List<LancamentoColaborador>();
}

public enum TipoLancamento { Entrada = 0, Saida = 1 }

public class LancamentoColaborador
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ColaboradorId { get; set; }
    public Colaborador? Colaborador { get; set; }
    public TipoLancamento Tipo { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public DateOnly Data { get; set; }
    /*  Referência ao mês/ano do holerite ("2026-09"). Se vazio, deriva de Data. */
    public string? ReferenciaMesAno { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
