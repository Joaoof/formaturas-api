namespace FormaturasFlow.Api.Payments;

public class CoraOptions
{
    public const string SectionName = "Cora";

    public bool   Sandbox             { get; set; } = true;
    public string ClientId            { get; set; } = string.Empty;
    public string CertificateBase64   { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public string WebhookSecret       { get; set; } = string.Empty;

    /*  A Cora usa mTLS: o mesmo host atende token e cobranças, e o
        certificado vai no CoraHttpHandler.  */
    public string BaseUrl => Sandbox
        ? "https://matls-clients.api.stage.cora.com.br"
        : "https://matls-clients.api.cora.com.br";
}
