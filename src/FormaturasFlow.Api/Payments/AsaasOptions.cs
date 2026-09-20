namespace FormaturasFlow.Api.Payments;

public class AsaasOptions
{
    public const string SectionName = "Asaas";

    public bool   Sandbox       { get; set; } = true;
    public string ApiKey        { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    public string BaseUrl => Sandbox
        ? "https://api-sandbox.asaas.com/v3"
        : "https://api.asaas.com/v3";
}
