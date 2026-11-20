namespace ImageAnalysis.Application.UseCases.GetAnalysisFindings;

/// <summary>
/// Achado de um analisador. Name é o código estável (ex.: metadata.editing-software);
/// o texto para o usuário fica em Description.
/// </summary>
public sealed record FindingResult(
    Guid Id,
    string Name,
    string Source,
    string Description,
    IReadOnlyList<EvidenceResult> Evidences);
