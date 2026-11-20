using ImageAnalysis.Domain.Entities.Common;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Domain.Entities;

/// <summary>
/// Metadados normalizados da imagem. O dump completo do ExifTool fica no storage,
/// referenciado por RawMetadataPath. Uma Analysis possui um único ImageMetadata (1:1).
/// </summary>
public class ImageMetadata : BaseEntity
{
    public Guid AnalysisId { get; private set; }

    // Chave relativa no storage (ex.: analysis-{id}/metadata.json), não caminho absoluto.
    public string RawMetadataPath { get; private set; } = null!;

    public ImageDimensions Dimensions { get; private set; } = null!;

    // Construtor para materialização pelo ORM.
    private ImageMetadata()
    {
    }

    internal ImageMetadata(Guid analysisId, string rawMetadataPath, ImageDimensions dimensions)
    {
        if (string.IsNullOrWhiteSpace(rawMetadataPath))
            throw new ArgumentException("O caminho do metadata.json no storage é obrigatório.", nameof(rawMetadataPath));

        ArgumentNullException.ThrowIfNull(dimensions);

        AnalysisId = analysisId;
        RawMetadataPath = rawMetadataPath;
        Dimensions = dimensions;
    }
}
