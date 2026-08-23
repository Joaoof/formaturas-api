namespace FormaturasFlow.Api.Domain;

public enum StatusParcela
{
    Pendente = 0,
    Pago = 1,
    Atrasado = 2,
    Cancelado = 3
}

public class Parcela
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContratoId { get; set; }
    public Contrato? Contrato { get; set; }

    public int Numero { get; set; }
    public decimal Valor { get; set; }
    public decimal ValorPago { get; set; }
    public DateOnly Vencimento { get; set; }
    public DateOnly? DataPagamento { get; set; }
    public StatusParcela Status { get; set; } = StatusParcela.Pendente;
    public string? FormaPagamento { get; set; }
    public string? Observacao { get; set; }

    public string? PspProvider { get; set; }
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
