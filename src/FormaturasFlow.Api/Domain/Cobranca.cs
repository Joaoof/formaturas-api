namespace FormaturasFlow.Api.Domain;

public enum StatusCobranca
{
    Pendente = 0,
    Confirmado = 1,
    Vencido = 2,
    Cancelado = 3,
    Estornado = 4
}

public class Cobranca
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ExternalReference { get; set; } = string.Empty;

    public string ClienteNome { get; set; } = string.Empty;
    public string? ClienteCpf { get; set; }
    public string? ClienteEmail { get; set; }
    public string? ClienteWhatsapp { get; set; }
    public string? ClienteTelefone { get; set; }

    public decimal Valor { get; set; }
    public decimal ValorPago { get; set; }
    public DateOnly Vencimento { get; set; }
    public DateOnly? DataPagamento { get; set; }
    public string Descricao { get; set; } = string.Empty;

    public string TipoPagamento { get; set; } = "cartao";
    public int? NumParcelasCartao { get; set; }
    public TipoEvento TipoEvento { get; set; } = TipoEvento.Formatura;

    public StatusCobranca Status { get; set; } = StatusCobranca.Pendente;

    public string? PspProvider { get; set; }
    public string? PspCustomerId { get; set; }
    public string? PspChargeId { get; set; }
    public string? PspStatus { get; set; }

    public string? BoletoUrl { get; set; }
    public string? BoletoLinhaDigitavel { get; set; }
    public string? BoletoCodigoBarras { get; set; }
    public string? PixCopiaCola { get; set; }
    public string? PixQrCodeUrl { get; set; }
    public string? LinkPagamento { get; set; }

    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AtualizadaEm { get; set; } = DateTimeOffset.UtcNow;
}
