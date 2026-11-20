namespace ImageAnalysis.Application.UseCases.GetAnalysisFindings;

/// <summary>
/// Evidência que sustenta um achado.
/// </summary>
public sealed record EvidenceResult(string Name, string Description, string Strength, string Direction);
