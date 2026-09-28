using System.Security.Cryptography;
using System.Text;

namespace FormaturasFlow.Api.Domain;

/*  Regras da assinatura eletrônica do contrato.

    Fica no domínio, e não no endpoint, porque a adesão pública e a
    assinatura avulsa precisam aplicar exatamente as mesmas regras — e
    porque é aqui que mora a única coisa que não pode divergir entre as
    duas: como o documento é reduzido a hash.  */
public static class AssinaturaContrato
{
    /*  Limite do PNG da rubrica.  Uma assinatura desenhada num canvas de
        celular não passa de algumas dezenas de KB; o teto existe para um
        POST anônimo não conseguir encher a tabela.  */
    public const int TamanhoMaximoImagem = 512 * 1024;

    private const string PrefixoPng = "data:image/png;base64,";

    /*  SHA-256 do texto do contrato, normalizado.

        A normalização de fim de linha é essencial: o navegador manda CRLF
        em textarea e o servidor guarda LF, então sem isso o mesmo documento
        geraria hashes diferentes e toda verificação apontaria adulteração
        onde não houve.  */
    public static string Hash(string? texto)
    {
        var normalizado = (texto ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizado)));
    }

    /*  Confere se o texto atual ainda é o que foi assinado.  */
    public static bool TextoIntacto(Contrato contrato) =>
        contrato.AssinaturaHashDocumento is null
        || string.Equals(contrato.AssinaturaHashDocumento, Hash(contrato.TextoContrato), StringComparison.OrdinalIgnoreCase);

    /*  Aceita só PNG em data URL, que é o que o canvas do navegador produz.
        Recusar o resto evita guardar SVG — que carrega script — num campo
        que depois é renderizado.  */
    public static void ValidarImagem(string? imagem)
    {
        if (string.IsNullOrWhiteSpace(imagem))
            throw new DadosInvalidosException("ASSINATURA_AUSENTE", "A assinatura é obrigatória.");

        if (!imagem.StartsWith(PrefixoPng, StringComparison.OrdinalIgnoreCase))
            throw new DadosInvalidosException("ASSINATURA_FORMATO_INVALIDO",
                "A assinatura precisa ser uma imagem PNG em data URL.");

        if (imagem.Length > TamanhoMaximoImagem)
            throw new DadosInvalidosException("ASSINATURA_MUITO_GRANDE",
                $"A assinatura excede {TamanhoMaximoImagem / 1024} KB.");

        var base64 = imagem[PrefixoPng.Length..];
        if (base64.Length == 0 || !Convert.TryFromBase64String(base64, new byte[base64.Length], out _))
            throw new DadosInvalidosException("ASSINATURA_FORMATO_INVALIDO", "A assinatura não é um base64 válido.");
    }

    /*  Aplica a assinatura.  Idempotente por decisão: reassinar um contrato
        já assinado é recusado, para não trocar em silêncio a prova de um
        aceite que já aconteceu.  */
    public static void Aplicar(
        Contrato contrato,
        string imagem,
        string? nome,
        string? cpf,
        string? ip,
        string? userAgent)
    {
        if (contrato.Assinado)
            throw new ConflitoException("CONTRATO_JA_ASSINADO",
                $"Este contrato já foi assinado em {contrato.AssinadoEm:dd/MM/yyyy HH:mm}.");

        ValidarImagem(imagem);

        contrato.AssinaturaImagem = imagem;
        contrato.AssinaturaHashDocumento = Hash(contrato.TextoContrato);
        contrato.AssinadoEm = DateTimeOffset.UtcNow;
        contrato.AssinadoIp = Limitar(ip, 64);
        contrato.AssinadoUserAgent = Limitar(userAgent, 512);
        contrato.AssinanteNome = Limitar(nome, 200);
        contrato.AssinanteCpf = Limitar(Digitos(cpf), 14);
        contrato.AtualizadoEm = DateTimeOffset.UtcNow;
    }

    private static string? Digitos(string? valor) =>
        valor is null ? null : new string(valor.Where(char.IsDigit).ToArray());

    private static string? Limitar(string? valor, int max) =>
        string.IsNullOrWhiteSpace(valor) ? null
        : valor.Length <= max ? valor
        : valor[..max];
}
