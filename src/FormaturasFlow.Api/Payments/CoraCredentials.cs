using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

/*  Credencial mTLS da Cora, resolvida UMA vez no arranque.

    Existe separada do handler por dois motivos: o certificado é caro de
    carregar e o handler é transiente; e o `client_id` do fluxo OAuth é o
    próprio CN do certificado — derivar aqui evita a classe de bug em que o
    .pem de produção é publicado com o client_id de stage no appsettings.  */
public sealed class CoraCredentials : IDisposable
{
    public CoraCredentials(IOptions<CoraOptions> opt)
    {
        var o = opt.Value;

        Certificado = CarregarCertificado(o);
        ClientId = !string.IsNullOrWhiteSpace(o.ClientId)
            ? o.ClientId.Trim()
            : Certificado is not null
                ? ClientIdDoCertificado(Certificado)
                : string.Empty;
    }

    public X509Certificate2? Certificado { get; }

    public string ClientId { get; }

    public bool Configurada => Certificado is not null && !string.IsNullOrWhiteSpace(ClientId);

    /*  Um certificado por processo, então não há vazamento crescente; o
        Dispose existe para devolver o handle da chave no desligamento
        gracioso.  */
    public void Dispose() => Certificado?.Dispose();

    /*  CN do subject.  A Cora emite "CN=int-<id da integração>", e esse id é
        exatamente o client_id aceito no POST /token.  */
    public static string ClientIdDoCertificado(X509Certificate2 cert)
    {
        var cn = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return string.IsNullOrWhiteSpace(cn) ? string.Empty : cn.Trim();
    }

    private static X509Certificate2? CarregarCertificado(CoraOptions o)
    {
        var certPem = LerPem(o.CertificatePemPath, o.CertificatePem);
        var keyPem = LerPem(o.PrivateKeyPemPath, o.PrivateKeyPem);

        if (certPem is not null && keyPem is not null)
            return DoPem(certPem, keyPem);

        if (!string.IsNullOrWhiteSpace(o.CertificateBase64))
            return X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(o.CertificateBase64),
                o.CertificatePassword);

        /*  Sem credencial: a aplicação sobe e só falha se alguém emitir
            cobrança pela Cora, com mensagem explícita no gateway.  */
        return null;
    }

    private static X509Certificate2 DoPem(string certPem, string keyPem)
    {
        using var comChave = X509Certificate2.CreateFromPem(certPem, keyPem);

        /*  Round-trip por PKCS#12: um certificado vindo de CreateFromPem
            carrega a chave "efêmera", que o SChannel do Windows recusa no
            handshake cliente.  Reimportar normaliza o comportamento entre
            Linux (dev/CI) e Windows.  */
        return X509CertificateLoader.LoadPkcs12(
            comChave.Export(X509ContentType.Pkcs12),
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);
    }

    /*  Caminho tem precedência sobre conteúdo inline.  O inline aceita PEM
        cru ou base64 porque variável de ambiente multi-linha quebra em boa
        parte dos orquestradores.  */
    private static string? LerPem(string caminho, string inline)
    {
        if (!string.IsNullOrWhiteSpace(caminho))
        {
            if (!File.Exists(caminho))
                throw new InvalidOperationException(
                    $"Certificado da Cora não encontrado em '{caminho}'. Confira Cora:CertificatePemPath / Cora:PrivateKeyPemPath.");

            return File.ReadAllText(caminho);
        }

        if (string.IsNullOrWhiteSpace(inline))
            return null;

        var valor = inline.Trim();
        if (valor.Contains("-----BEGIN", StringComparison.Ordinal))
            return valor;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(valor));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Cora:CertificatePem/PrivateKeyPem não é PEM nem base64 válido.");
        }
    }
}
