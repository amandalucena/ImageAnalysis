using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Subconjunto dos metadados usado pelas regras de achados, já limpo e tipado.
/// Não interpreta nada: ausência de um campo é null (ou false/lista vazia), nunca uma conclusão.
/// Fica em memória; a persistência destes campos é escopo da #56.
/// </summary>
public sealed record NormalizedMetadata
{
    public string? Make { get; init; }
    public string? Model { get; init; }

    // Todas as fontes encontradas, cada uma com a tag de origem: valores divergentes
    // entre EXIF e XMP são um sinal, e escolher um só esconderia isso.
    public IReadOnlyList<SoftwareTag> Software { get; init; } = [];

    public MetadataDate? DateTimeOriginal { get; init; }
    public MetadataDate? CreateDate { get; init; }
    public MetadataDate? ModifyDate { get; init; }

    public bool HasJfif { get; init; }
    public string? JfifVersion { get; init; }

    public bool HasThumbnail { get; init; }

    // Só a presença de coordenadas; as coordenadas em si ficam fora (#56).
    public bool HasGps { get; init; }

    // Dimensões reais, lidas da estrutura da imagem.
    public ImageDimensions? Dimensions { get; init; }

    // Dimensões declaradas no EXIF na captura. Diferença em relação às reais pode indicar
    // redimensionamento ou recorte depois da captura.
    public ImageDimensions? DeclaredDimensions { get; init; }

    // Avisos do ExifTool ao ler o arquivo (ex.: "JPEG format error").
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Valor de software (Software, CreatorTool) e a tag de onde veio, ex.: "IFD0:Software".
/// </summary>
public sealed record SoftwareTag(string Source, string Value);

/// <summary>
/// Data dos metadados em três partes. Raw é sempre preservado; Value é null quando o texto
/// não é uma data válida (ex.: "0000:00:00 00:00:00", gravado por câmeras sem relógio).
/// Value não tem fuso: o EXIF guarda o offset em outra tag, e sem ela o fuso é desconhecido.
/// </summary>
public sealed record MetadataDate(string Raw, DateTime? Value, TimeSpan? Offset);
