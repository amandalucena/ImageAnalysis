namespace ImageAnalysis.Application.UseCases.GetAnalysisFindings;

/// <summary>
/// Saída do GetAnalysisFindings. O status permite ao cliente saber se a lista já é definitiva:
/// ela fica vazia enquanto a análise está em Pending ou Processing, e também em Failed, porque
/// uma análise que falha não grava achados.
/// </summary>
public sealed record AnalysisFindingsResult(Guid AnalysisId, string Status, IReadOnlyList<FindingResult> Findings);
