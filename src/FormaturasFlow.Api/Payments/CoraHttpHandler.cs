using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace FormaturasFlow.Api.Payments;

public class CoraHttpHandler : HttpClientHandler
{
    public CoraHttpHandler(IOptions<CoraOptions> opt)
    {
        var o = opt.Value;

        if (!string.IsNullOrWhiteSpace(o.CertificateBase64))
        {
            var raw = Convert.FromBase64String(o.CertificateBase64);
            var cert = X509CertificateLoader.LoadPkcs12(raw, o.CertificatePassword);
            ClientCertificates.Add(cert);
            ClientCertificateOptions = ClientCertificateOption.Manual;
        }
    }
}
