namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Segmento APPn identificado pelo texto no início do conteúdo. O conteúdo do EXIF e do XMP não
/// é interpretado: isso é do MetadataAnalyzer, e os analisadores são independentes (CLAUDE.md 8.1).
/// Só os cabeçalhos curtos de JFIF, ICC e Adobe são lidos.
/// </summary>
public sealed record JpegApplicationSegment
{
    public const int MaxIdentifierLength = 64;

    public JpegMarker Marker { get; }

    // Posição do marcador e valor do campo de comprimento, como no JpegSegment.
    public long Offset { get; }
    public int Length { get; }

    public JpegApplicationKind Kind { get; }

    // Texto ASCII imprimível do início do conteúdo (ex.: "Exif", "Ducky"), até o primeiro byte
    // fora dessa faixa. null quando o conteúdo não começa com texto. Guardado também nos tipos
    // conhecidos, para que um APPn desconhecido possa ser reconhecido depois sem reler o arquivo.
    public string? Identifier { get; }

    public JfifHeader? Jfif { get; init; }
    public IccProfileChunk? IccProfileChunk { get; init; }
    public AdobeHeader? Adobe { get; init; }

    public JpegApplicationSegment(JpegMarker marker, long offset, int length, JpegApplicationKind kind, string? identifier)
    {
        if (!JpegMarkers.IsApplication((byte)marker))
            throw new ArgumentException("O segmento deve vir de um marcador APPn.", nameof(marker));

        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "A posição do segmento não pode ser negativa.");

        if (length is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(length), "O comprimento do segmento deve caber em 16 bits.");

        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "Tipo de segmento inválido.");

        if (identifier is { Length: 0 or > MaxIdentifierLength })
            throw new ArgumentException(
                $"O identificador deve ter entre 1 e {MaxIdentifierLength} caracteres.", nameof(identifier));

        Marker = marker;
        Offset = offset;
        Length = length;
        Kind = kind;
        Identifier = identifier;
    }
}

public enum JpegApplicationKind
{
    Unknown = 0,
    Jfif = 1,          // APP0 "JFIF"
    JfifExtension = 2, // APP0 "JFXX"
    Exif = 3,          // APP1 "Exif"
    Xmp = 4,           // APP1 "http://ns.adobe.com/xap/1.0/"
    ExtendedXmp = 5,   // APP1 "http://ns.adobe.com/xmp/extension/"
    IccProfile = 6,    // APP2 "ICC_PROFILE"
    Mpf = 7,           // APP2 "MPF"
    Photoshop = 8,     // APP13 "Photoshop 3.0" (IPTC e recursos 8BIM)
    Adobe = 9          // APP14 "Adobe"
}

/// <summary>
/// Cabeçalho JFIF (APP0). Densidade em pontos por polegada ou por centímetro, conforme Units
/// (0 = só proporção, 1 = dpi, 2 = dpcm).
/// </summary>
public sealed record JfifHeader(
    byte MajorVersion,
    byte MinorVersion,
    byte Units,
    ushort XDensity,
    ushort YDensity,
    byte ThumbnailWidth,
    byte ThumbnailHeight);

/// <summary>
/// Pedaço do perfil ICC (APP2): perfis grandes são divididos em vários segmentos numerados de 1
/// até Count.
/// </summary>
public sealed record IccProfileChunk(byte Sequence, byte Count);

/// <summary>
/// Cabeçalho Adobe (APP14). Transform diz como as cores foram gravadas: 0 = sem transformação
/// (RGB ou CMYK), 1 = YCbCr, 2 = YCCK.
/// </summary>
public sealed record AdobeHeader(ushort Version, ushort Flags0, ushort Flags1, byte Transform);
