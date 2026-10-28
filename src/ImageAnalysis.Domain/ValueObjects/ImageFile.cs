namespace ImageAnalysis.Domain.ValueObjects;

/// <summary>
/// Imagem original enviada para análise. O arquivo fica no storage; aqui ficam
/// apenas sua referência e os dados usados para auditoria.
/// A imagem original nunca é sobrescrita durante o processamento.
/// </summary>
public sealed record ImageFile
{
    public const int MaxOriginalFileNameLength = 255;

    public string OriginalFileName { get; }
    public string StoragePath { get; }
    public long SizeInBytes { get; }

    // Hash do arquivo no momento do upload; permite provar que a imagem analisada não foi alterada.
    public string Sha256 { get; }

    // MIME detectado pelo conteúdo do arquivo, não pela extensão.
    public string MimeType { get; }

    public ImageFile(string originalFileName, string storagePath, long sizeInBytes, string sha256, string mimeType)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("O nome original do arquivo é obrigatório.", nameof(originalFileName));

        if (originalFileName.Length > MaxOriginalFileNameLength)
            throw new ArgumentException(
                $"O nome original do arquivo deve ter no máximo {MaxOriginalFileNameLength} caracteres.", nameof(originalFileName));

        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("O caminho da imagem no storage é obrigatório.", nameof(storagePath));

        if (sizeInBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "O tamanho do arquivo deve ser maior que zero.");

        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("O SHA-256 deve ter 64 caracteres hexadecimais.", nameof(sha256));

        if (string.IsNullOrWhiteSpace(mimeType))
            throw new ArgumentException("O MIME type é obrigatório.", nameof(mimeType));

        OriginalFileName = originalFileName;
        StoragePath = storagePath;
        SizeInBytes = sizeInBytes;
        Sha256 = sha256.ToLowerInvariant();
        MimeType = mimeType;
    }
}
