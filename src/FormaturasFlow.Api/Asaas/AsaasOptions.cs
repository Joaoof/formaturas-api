namespace FormaturasFlow.Api.Asaas;

public class AsaasOptions
{
    public const string SectionName = "Asaas";

    public bool Sandbox { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookToken { get; set; } = string.Empty;

    public string BaseUrl => Sandbox
        ? "https://sandbox.asaas.com/api/v3"
        : "https://api.asaas.com/v3";
}
