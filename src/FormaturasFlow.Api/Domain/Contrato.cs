namespace FormaturasFlow.Api.Domain;

public class Contrato
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AlunoId { get; set; }
    public Aluno? Aluno { get; set; }

    public string? Pacote { get; set; }
    public decimal ValorTotal { get; set; }
    public decimal ValorEntrada { get; set; }
    public decimal Desconto { get; set; }
    public int NumParcelas { get; set; } = 1;
    public int? DiaVencimento { get; set; }
    public bool AutorizaImagem { get; set; } = true;
    public string? FormaPagamento { get; set; }
    public DateOnly DataContrato { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string? TextoContrato { get; set; }

    /*  Assinatura eletrônica do contratante.

        `AssinaturaHashDocumento` é o SHA-256 do texto EXATO que estava na
        tela no momento do aceite.  É o que dá valor de prova ao conjunto:
        sem ele, alguém poderia editar as cláusulas depois e a assinatura
        continuaria lá, aparentando cobrir um texto que o formando nunca
        leu.  Com ele, qualquer alteração posterior é detectável.

        IP e user agent formam a trilha de auditoria exigida para
        caracterizar autoria na assinatura eletrônica simples
        (MP 2.200-2/2001, art. 10, §2º).  */
    public string? AssinaturaImagem { get; set; }
    public string? AssinaturaHashDocumento { get; set; }
    public DateTimeOffset? AssinadoEm { get; set; }
    public string? AssinadoIp { get; set; }
    public string? AssinadoUserAgent { get; set; }
    public string? AssinanteNome { get; set; }
    public string? AssinanteCpf { get; set; }

    public bool Assinado => AssinadoEm is not null;

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Parcela> Parcelas { get; set; } = new List<Parcela>();
}
