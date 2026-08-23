namespace FormaturasFlow.Api.Efi;

public class EfiOptions
{
    public const string SectionName = "Efi";

    public bool Sandbox { get; set; } = true;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public string CertificateBase64 { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;

    public string PixKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public string CobrancasBaseUrl => Sandbox
        ? "https://cobrancas-h.api.efipay.com.br"
        : "https://cobrancas.api.efipay.com.br";

    public string PixBaseUrl => Sandbox
        ? "https://pix-h.api.efipay.com.br"
        : "https://pix.api.efipay.com.br";
}
