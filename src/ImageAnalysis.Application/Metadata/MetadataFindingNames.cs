namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Nomes estáveis dos achados do MetadataAnalyzer. São gravados em Finding.Name
/// e usados pelas regras do Evidence Aggregator (#43), então não devem mudar.
/// O texto exibido ao usuário fica na descrição do achado, não aqui.
/// </summary>
public static class MetadataFindingNames
{
    public const string EditingSoftware = "metadata.editing-software";
    public const string AiGenerator = "metadata.ai-generator";
    public const string InconsistentDates = "metadata.inconsistent-dates";
    public const string StrippedMetadata = "metadata.stripped-metadata";
    public const string MetadataUnavailable = "metadata.unavailable";
    public const string ReadWarnings = "metadata.read-warnings";
}
