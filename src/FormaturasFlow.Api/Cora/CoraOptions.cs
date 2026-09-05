namespace FormaturasFlow.Api.Cora;

public class CoraOptions
{
    public const string SectionName = "Cora";

    public bool Sandbox { get; set; } = true;
    public string ClientId { get; set; } = string.Empty;
    public string CertificateBase64 { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public string WebhookToken { get; set; } = string.Empty;

    public string BaseUrl => Sandbox
        ? "https://matls-clients.api.stage.cora.com.br"
        : "https://matls-clients.api.cora.com.br";
}
