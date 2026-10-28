namespace ImageAnalysis.Application.UseCases.GetAnalysisStatus;

/// <summary>
/// Entrada do GetAnalysisStatus: o id devolvido na criação da análise.
/// </summary>
public sealed record GetAnalysisStatusQuery(Guid AnalysisId);
