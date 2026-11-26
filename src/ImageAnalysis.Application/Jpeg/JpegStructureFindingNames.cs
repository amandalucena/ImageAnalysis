namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Nomes estáveis dos achados do JpegStructureAnalyzer. São gravados em Finding.Name e usados
/// pelas regras do Evidence Aggregator (#43), então não devem mudar. O texto exibido ao usuário
/// fica na descrição do achado, não aqui.
/// </summary>
public static class JpegStructureFindingNames
{
    public const string Truncated = "jpeg.truncated";
    public const string MalformedStructure = "jpeg.malformed-structure";
    public const string UnexpectedMarkers = "jpeg.unexpected-markers";
    public const string TrailingData = "jpeg.trailing-data";
    public const string EditorSignatures = "jpeg.editor-signatures";
    public const string EncodingProfile = "jpeg.encoding-profile";
}

/// <summary>
/// Nomes estáveis das evidências dos achados da estrutura JPEG (ver JpegStructureFindingNames).
/// </summary>
public static class JpegStructureEvidenceNames
{
    // jpeg.truncated
    public const string TruncatedFile = "truncated-file";
    public const string MissingEndOfImage = "missing-eoi";

    // jpeg.malformed-structure
    public const string UnexpectedData = "unexpected-data";
    public const string InvalidSegmentLength = "invalid-segment-length";
    public const string MalformedSegment = "malformed-segment";
    public const string UnknownMarker = "unknown-marker";

    // jpeg.unexpected-markers
    public const string RepeatedStartOfImage = "repeated-soi";
    public const string MultipleFrames = "multiple-frames";
    public const string RestartOutsideScan = "restart-outside-scan";
    public const string ScanBeforeFrame = "scan-before-frame";
    public const string ImageWithoutScan = "image-without-scan";
    public const string UndefinedQuantizationTable = "undefined-quantization-table";

    // jpeg.trailing-data
    public const string MpfImage = "mpf-image";
    public const string AppendedJpeg = "appended-jpeg";
    public const string AppendedVideo = "appended-video";
    public const string ZeroPadding = "zero-padding";
    public const string UnknownTrailingData = "unknown-trailing-data";

    // jpeg.editor-signatures
    public const string EncoderComment = "encoder-comment";
    public const string PhotoshopResources = "photoshop-resources";
    public const string AdobeSegment = "adobe-segment";

    // jpeg.encoding-profile
    public const string FrameProcess = "frame-process";
    public const string ChromaSubsampling = "chroma-subsampling";
    public const string ProgressiveScans = "progressive-scans";
    public const string HuffmanTables = "huffman-tables";
    public const string RestartInterval = "restart-interval";
    public const string JfifVersion = "jfif-version";
    public const string AdobeTransform = "adobe-transform";
    public const string Comment = "comment";

    // Qualquer achado: ocorrências além do limite de evidências.
    public const string MoreOccurrences = "more-occurrences";
}
