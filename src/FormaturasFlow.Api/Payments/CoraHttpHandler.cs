using System.Security.Cryptography.X509Certificates;

namespace FormaturasFlow.Api.Payments;

/*  Handler mTLS: a Cora só responde no host matls-clients.* e exige o
    certificado do cliente já no handshake — inclusive no /token.  */
public class CoraHttpHandler : HttpClientHandler
{
    public CoraHttpHandler(CoraCredentials credenciais)
    {
        if (credenciais.Certificado is not null)
        {
            ClientCertificates.Add(credenciais.Certificado);
            ClientCertificateOptions = ClientCertificateOption.Manual;
        }
    }
}
