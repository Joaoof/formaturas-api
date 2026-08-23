namespace FormaturasFlow.Api.Domain;

public class WebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset RecebidoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessadoEm { get; set; }
    public string? Erro { get; set; }
}
