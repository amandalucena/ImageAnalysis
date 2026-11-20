namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Nomes estáveis das evidências dos achados de metadados (ver MetadataFindingNames).
/// </summary>
public static class MetadataEvidenceNames
{
    public const string EditingSoftwareTag = "editing-software-tag";
    public const string SoftwareDivergence = "software-divergence";
    public const string AiGeneratorTag = "ai-generator-tag";
    public const string ModifyBeforeCapture = "modify-before-capture";
    public const string CreateOriginalMismatch = "create-original-mismatch";
    public const string ScannedOriginalDate = "scanned-original-date";
    public const string ModifiedAfterCapture = "modified-after-capture";
    public const string DateNotComparable = "date-not-comparable";
    public const string MetadataAbsent = "metadata-absent";
    public const string CameraIdentityMissing = "camera-identity-missing";
    public const string ThumbnailMissing = "thumbnail-missing";
    public const string EditorWithoutCamera = "editor-without-camera";
    public const string ExifToolWarning = "exiftool-warning";
}
