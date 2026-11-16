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

    // Tags gravadas pela câmera em instantes levemente diferentes podem divergir em 1-2 s.
    public static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(2);

    // Celulares e apps regravam o arquivo logo após a captura; abaixo disso não é edição.
    public static readonly TimeSpan ModifiedAfterCaptureThreshold = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Datas inconsistentes entre DateTimeOriginal, CreateDate e ModifyDate. Datas ausentes ou
    /// inválidas não são comparadas: ausência não é evidência (CLAUDE.md, seção 9).
    /// Em fotos escaneadas, DateTimeOriginal é a data da foto e CreateDate a do scan, então a
    /// diferença entre elas é esperada (Neutral) e a referência de captura passa a ser a CreateDate.
    /// </summary>
    public static AnalyzerFinding? InconsistentDates(NormalizedMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var device = Device(metadata);
        var evidences = new List<AnalyzerEvidence>();

        // (b) CreateDate x DateTimeOriginal
        var createVsOriginal = Difference(metadata.CreateDate, metadata.DateTimeOriginal);
        if (createVsOriginal is { } diff && diff.Duration() > DateTolerance)
            evidences.Add(CreateOriginalEvidence(metadata, device));

        // (a) e (c) ModifyDate x captura
        var (captureTag, capture) = CaptureReference(metadata, device);
        if (Difference(metadata.ModifyDate, capture) is { } sinceCapture)
        {
            if (sinceCapture < -DateTolerance)
                evidences.Add(new AnalyzerEvidence(
                    MetadataEvidenceNames.ModifyBeforeCapture,
                    $"ModifyDate ({metadata.ModifyDate!.Raw}) é anterior a {captureTag} ({capture!.Raw}): " +
                    "o arquivo não pode ter sido modificado antes de ser capturado.",
                    EvidenceStrength.Medium,
                    EvidenceDirection.SupportsManipulation));
            else if (sinceCapture > ModifiedAfterCaptureThreshold)
                evidences.Add(new AnalyzerEvidence(
                    MetadataEvidenceNames.ModifiedAfterCapture,
                    $"ModifyDate ({metadata.ModifyDate!.Raw}) é posterior a {captureTag} ({capture!.Raw}): " +
                    "o arquivo foi regravado depois da captura, o que pode indicar edição.",
                    EvidenceStrength.Low,
                    EvidenceDirection.SupportsManipulation));
        }

        if (evidences.Count == 0)
            return null;

        return new AnalyzerFinding(
            MetadataFindingNames.InconsistentDates,
            "As datas registradas nos metadados não são consistentes entre si.",
            evidences);
    }

    private static AnalyzerEvidence CreateOriginalEvidence(NormalizedMetadata metadata, CaptureDevice? device)
    {
        var dates = $"CreateDate ({metadata.CreateDate!.Raw}) difere de DateTimeOriginal ({metadata.DateTimeOriginal!.Raw})";

        return device switch
        {
            CaptureDevice.Scanner => new AnalyzerEvidence(
                MetadataEvidenceNames.ScannedOriginalDate,
                $"{dates}. A imagem vem de um scanner: DateTimeOriginal é a data da foto original e " +
                "CreateDate a do scan, então a diferença é esperada e não indica manipulação.",
                EvidenceStrength.Low,
                EvidenceDirection.Neutral),
            CaptureDevice.Camera => new AnalyzerEvidence(
                MetadataEvidenceNames.CreateOriginalMismatch,
                $"{dates}. Em câmeras digitais as duas datas são iguais.",
                EvidenceStrength.Medium,
                EvidenceDirection.SupportsManipulation),
            _ => new AnalyzerEvidence(
                MetadataEvidenceNames.CreateOriginalMismatch,
                $"{dates}. Os metadados não informam se a imagem veio de câmera ou de scanner: em fotos " +
                "escaneadas essa diferença é esperada (data da foto original x data do scan).",
                EvidenceStrength.Low,
                EvidenceDirection.SupportsManipulation)
        };
    }

    // FileSource vence; sem ele, Make/Model de scanner conhecido.
    private static CaptureDevice? Device(NormalizedMetadata metadata) =>
        metadata.CaptureDevice
        ?? (KnownSoftware.IsScanner(metadata.Make) || KnownSoftware.IsScanner(metadata.Model)
            ? CaptureDevice.Scanner
            : null);

    // Câmera: DateTimeOriginal. Scanner: CreateDate (momento do scan). Cai para a outra se faltar.
    private static (string Tag, MetadataDate? Date) CaptureReference(NormalizedMetadata metadata, CaptureDevice? device)
    {
        var original = ("DateTimeOriginal", metadata.DateTimeOriginal);
        var create = ("CreateDate", metadata.CreateDate);

        var (first, second) = device == CaptureDevice.Scanner ? (create, original) : (original, create);
        return first.Item2?.Value is not null ? first : second;
    }

    // later - earlier. null se alguma não tem valor. Com offset nas duas, compara em UTC;
    // senão compara sem fuso, porque a câmera grava todas no mesmo relógio local.
    private static TimeSpan? Difference(MetadataDate? later, MetadataDate? earlier)
    {
        if (later?.Value is not { } laterValue || earlier?.Value is not { } earlierValue)
            return null;

        if (later.Offset is { } laterOffset && earlier.Offset is { } earlierOffset)
            return (laterValue - laterOffset) - (earlierValue - earlierOffset);

        return laterValue - earlierValue;
    }

    // Diferença só de maiúsculas ou espaços nas pontas não conta como divergência.
    private static bool HasDivergentValues(IReadOnlyList<SoftwareTag> software) =>
        software
            .Select(tag => tag.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();
}
