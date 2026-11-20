namespace ImageAnalysis.Application.UseCases.GetAnalysisFindings;

/// <summary>
/// Entrada do GetAnalysisFindings: o id devolvido na criação da análise.
/// </summary>
public sealed record GetAnalysisFindingsQuery(Guid AnalysisId);
