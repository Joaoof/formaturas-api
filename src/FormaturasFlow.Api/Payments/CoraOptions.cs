namespace FormaturasFlow.Api.Payments;

public class CoraOptions
{
    public const string SectionName = "Cora";

    public bool Sandbox { get; set; } = true;

    /*  Opcional: quando vazio, é lido do CN do certificado (a Cora emite o
        certificado com CN = id da integração, ex. "int-5lyChzouRAaYtLbzKxXJbd").
        Preencher só faz sentido para forçar um id diferente do certificado.  */
    public string ClientId { get; set; } = string.Empty;

    /*  A Cora entrega as credenciais como PAR PEM (certificate.pem +
        private-key.key).  Caminho em disco é o formato preferido; o conteúdo
        inline existe para deploys sem volume (Railway, Fly, container),
        aceitando o PEM puro ou o mesmo PEM em base64.  */
    public string CertificatePemPath { get; set; } = string.Empty;
    public string PrivateKeyPemPath { get; set; } = string.Empty;
    public string CertificatePem { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;

    /*  Alternativa PKCS#12, caso o par PEM seja convertido para .pfx.  */
    public string CertificateBase64 { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    /*  A Cora usa mTLS: o mesmo host atende token e cobranças, e o
        certificado vai no CoraHttpHandler.  */
    public string BaseUrl => Sandbox
        ? "https://matls-clients.api.stage.cora.com.br"
        : "https://matls-clients.api.cora.com.br";
}
