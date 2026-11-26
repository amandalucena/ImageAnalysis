namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Estrutura do arquivo JPEG como lida, sem interpretação: a avaliação do que é anômalo fica
/// nas regras do JpegStructureAnalyzer (22.4) e no CompressionAnalyzer (#23). Fica em memória;
/// não é persistida.
/// </summary>
public sealed record JpegStructure
{
    // Na ordem em que aparecem no arquivo.
    public IReadOnlyList<JpegSegment> Segments { get; }

    // Um arquivo comum tem um só; mais de um ocorre em JPEG hierárquico ou num arquivo anômalo.
    public IReadOnlyList<JpegFrameHeader> Frames { get; }

    // Todas as tabelas, na ordem do arquivo, inclusive as que redefinem um Id já visto.
    public IReadOnlyList<QuantizationTable> QuantizationTables { get; }

    // Posição do marcador EOI do fim do scan; null quando o arquivo termina sem ele.
    public long? EndOfImageOffset { get; }

    // Bytes depois do EOI. Não é por si só sinal de edição: celulares gravam ali a segunda
    // imagem do MPF e alguns editores anexam dados próprios.
    public long TrailingDataLength { get; }

    public IReadOnlyList<JpegReadIssue> Issues { get; }

    public JpegStructure(
        IEnumerable<JpegSegment> segments,
        IEnumerable<JpegFrameHeader> frames,
        IEnumerable<QuantizationTable> quantizationTables,
        long? endOfImageOffset,
        long trailingDataLength,
        IEnumerable<JpegReadIssue> issues)
    {
        if (endOfImageOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(endOfImageOffset), "A posição do EOI não pode ser negativa.");

        if (trailingDataLength < 0)
            throw new ArgumentOutOfRangeException(nameof(trailingDataLength), "O tamanho dos dados após o EOI não pode ser negativo.");

        if (endOfImageOffset is null && trailingDataLength != 0)
            throw new ArgumentException("Sem EOI não há dados após o EOI.", nameof(trailingDataLength));

        Segments = CopyWithoutNulls(segments, nameof(segments));
        Frames = CopyWithoutNulls(frames, nameof(frames));
        QuantizationTables = CopyWithoutNulls(quantizationTables, nameof(quantizationTables));
        EndOfImageOffset = endOfImageOffset;
        TrailingDataLength = trailingDataLength;
        Issues = CopyWithoutNulls(issues, nameof(issues));
    }

    public bool HasEndOfImage => EndOfImageOffset is not null;

    private static T[] CopyWithoutNulls<T>(IEnumerable<T> items, string paramName) where T : class
    {
        ArgumentNullException.ThrowIfNull(items, paramName);

        var copy = items.ToArray();
        if (copy.Any(i => i is null))
            throw new ArgumentException("A lista não pode conter itens nulos.", paramName);

        return copy;
    }
}

/// <summary>
/// Problema encontrado ao ler a estrutura. O leitor registra e segue (ou para, quando não há
/// como continuar) em vez de lançar exceção: um arquivo malformado também é uma evidência.
/// </summary>
public sealed record JpegReadIssue
{
    public JpegReadIssueKind Kind { get; }

    // Posição no arquivo onde o problema foi detectado.
    public long Offset { get; }

    public string Description { get; }

    // Quantidade de bytes afetados, quando se aplica: em UnexpectedData, os bytes ignorados
    // até o próximo marcador. Guardado à parte da descrição para que as regras (22.4) usem o valor.
    public long? Length { get; }

    // Marcador envolvido, quando se aplica: o código desconhecido (UnknownMarker), o segmento
    // com comprimento inválido (InvalidSegmentLength) ou onde a leitura recomeçou (UnexpectedData).
    public byte? MarkerCode { get; }

    public JpegReadIssue(
        JpegReadIssueKind kind, long offset, string description, long? length = null, byte? markerCode = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "Tipo de problema inválido.");

        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "A posição do problema não pode ser negativa.");

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A descrição do problema é obrigatória.", nameof(description));

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "A quantidade de bytes não pode ser negativa.");

        Kind = kind;
        Offset = offset;
        Description = description;
        Length = length;
        MarkerCode = markerCode;
    }
}

public enum JpegReadIssueKind
{
    // O arquivo não começa com 0xFF 0xD8.
    MissingStartOfImage = 0,

    // Código fora dos marcadores da T.81.
    UnknownMarker = 1,

    // Campo de comprimento menor que 2 ou que passa do fim do arquivo. Pelos bytes, um arquivo
    // cortado e um comprimento corrompido não se distinguem; a leitura para nos dois casos.
    InvalidSegmentLength = 2,

    // O arquivo acaba no meio de um marcador, de um campo de comprimento ou dos dados comprimidos.
    TruncatedFile = 3,

    // Bytes entre segmentos que não começam um marcador. A leitura recomeça no próximo marcador
    // conhecido, que pode estar dentro de outro segmento (ex.: a miniatura do EXIF): o que vem
    // depois deste problema é menos confiável.
    UnexpectedData = 4
}
