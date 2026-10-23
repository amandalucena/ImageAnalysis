namespace ImageAnalysis.Domain.Enums;

/// <summary>
/// Analisador que gerou o achado.
/// </summary>
public enum SourceFindingType
{
    Metadata = 0,
    JpegStructure = 1,
    Compression = 2,
    Ela = 3,
    C2pa = 4,
    AiDetector = 5
}
