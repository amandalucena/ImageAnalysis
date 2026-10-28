using System.Security.Cryptography;

namespace ImageAnalysis.Application.Service;

/// <summary>
/// Calcula o SHA-256 do arquivo enviado. O hash é registrado no upload e permite provar
/// depois que a imagem analisada é exatamente a recebida (cadeia de custódia).
/// Identifica os bytes do arquivo, não o conteúdo visual: uma foto salva de novo gera outro hash.
/// </summary>
public static class ImageHasher
{
    // Lê o stream desde o início e o devolve posicionado no início, para que as etapas
    // seguintes (ex.: gravação no storage) usem exatamente os mesmos bytes do hash.
    public static async Task<string> ComputeSha256Async(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanSeek)
            throw new ArgumentException("O stream precisa permitir voltar ao início.", nameof(content));

        content.Position = 0;
        var hash = await SHA256.HashDataAsync(content, cancellationToken);
        content.Position = 0;

        return Convert.ToHexStringLower(hash);
    }
}
