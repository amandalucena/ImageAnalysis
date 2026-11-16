using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Regras que transformam metadados normalizados em achados. Funções puras: não leem
/// arquivo nem chamam ferramenta, o que permite testá-las com NormalizedMetadata montado à mão.
/// Cada regra devolve null quando não há nada a relatar.
/// </summary>
public static class MetadataFindingRules
{
    /// <summary>
    /// Software de edição em Software/CreatorTool. Editor é Medium, não High: Lightroom e
    /// apps de celular também fazem ajustes legítimos. Valores divergentes entre as fontes
    /// reforçam o achado (Low), mas sozinhos não o geram.
    /// </summary>
    public static AnalyzerFinding? EditingSoftware(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var evidences = metadata.Software
            .Where(tag => KnownSoftware.IsEditor(tag.Value))
            .Select(tag => new AnalyzerEvidence(
                MetadataEvidenceNames.EditingSoftwareTag,
                $"{tag.Source} indica editor de imagem: {tag.Value}",
                EvidenceStrength.Medium,
                EvidenceDirection.SupportsManipulation))
            .ToList();

        if (evidences.Count == 0)
            return null;

        if (HasDivergentValues(metadata.Software))
        {
            var sources = string.Join("; ", metadata.Software.Select(tag => $"{tag.Source} = {tag.Value}"));

            evidences.Add(new AnalyzerEvidence(
                MetadataEvidenceNames.SoftwareDivergence,
                $"As fontes de software dos metadados divergem: {sources}",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation));
        }

        return new AnalyzerFinding(
            MetadataFindingNames.EditingSoftware,
            "Os metadados indicam que a imagem passou por um editor de imagem.",
            evidences);
    }

    /// <summary>
    /// Gerador de imagem por IA em Software/CreatorTool. É o sinal mais direto que os
    /// metadados oferecem (High), mas não prova: metadados podem ser forjados, e quem pondera
    /// isso é o Evidence Aggregator (#43).
    /// </summary>
    public static AnalyzerFinding? AiGenerator(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var evidences = metadata.Software
            .Where(tag => KnownSoftware.IsAiGenerator(tag.Value))
            .Select(tag => new AnalyzerEvidence(
                MetadataEvidenceNames.AiGeneratorTag,
                $"{tag.Source} indica gerador de imagem por IA: {tag.Value}",
                EvidenceStrength.High,
                EvidenceDirection.SupportsManipulation))
            .ToList();

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            MetadataFindingNames.AiGenerator,
            "Os metadados indicam que a imagem foi criada ou alterada por um gerador de imagem por IA.",
            evidences);
    }

    // Diferença só de maiúsculas ou espaços nas pontas não conta como divergência.
    private static bool HasDivergentValues(IReadOnlyList<SoftwareTag> software) =>
        software
            .Select(tag => tag.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();
}
