using ImageAnalysis.Domain.Entities.Common;

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

    public int Width { get; private set; }
    public int Height { get; private set; }

    // Construtor para materialização pelo ORM.
    private ImageMetadata()
    {
    }

    internal ImageMetadata(Guid analysisId, string rawMetadataPath, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(rawMetadataPath))
            throw new ArgumentException("O caminho do metadata.json no storage é obrigatório.", nameof(rawMetadataPath));

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A largura da imagem deve ser maior que zero.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "A altura da imagem deve ser maior que zero.");

        AnalysisId = analysisId;
        RawMetadataPath = rawMetadataPath;
        Width = width;
        Height = height;
    }
}
