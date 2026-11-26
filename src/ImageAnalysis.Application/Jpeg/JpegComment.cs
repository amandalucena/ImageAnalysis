namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Comentário (COM). Encoders costumam se identificar aqui, ex.:
/// "CREATOR: gd-jpeg v1.0 (using IJG JPEG v62), quality = 75". O texto é limitado a
/// MaxTextLength bytes; um comentário pode ter até 64 KB.
/// </summary>
public sealed record JpegComment
{
    public const int MaxTextLength = 1024;

    // Posição do marcador COM.
    public long Offset { get; }

    // Decodificado como Latin-1 (um caractere por byte, sem erro de decodificação), sem os
    // bytes nulos do fim.
    public string Text { get; }

    // Tamanho do conteúdo no arquivo, em bytes.
    public int DataLength { get; }

    public JpegComment(long offset, string text, int dataLength)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "A posição do comentário não pode ser negativa.");

        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > MaxTextLength)
            throw new ArgumentException($"O texto deve ter no máximo {MaxTextLength} caracteres.", nameof(text));

        if (dataLength < 0)
            throw new ArgumentOutOfRangeException(nameof(dataLength), "O tamanho do comentário não pode ser negativo.");

        Offset = offset;
        Text = text;
        DataLength = dataLength;
    }

    public bool IsTruncated => DataLength > MaxTextLength;
}
