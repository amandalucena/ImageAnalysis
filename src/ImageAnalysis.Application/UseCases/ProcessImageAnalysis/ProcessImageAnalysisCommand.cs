namespace ImageAnalysis.Application.UseCases.ProcessImageAnalysis;

/// <summary>
/// Entrada do ProcessImageAnalysis: o id recebido na mensagem da fila.
/// </summary>
public sealed record ProcessImageAnalysisCommand(Guid AnalysisId);
