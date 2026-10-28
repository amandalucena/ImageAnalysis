namespace ImageAnalysis.Application.UseCases.CreateImageAnalysis;

/// <summary>
/// Saída do CreateImageAnalysis: o id da análise criada, usado para consultar o status.
/// O id do upload (parte da chave no storage) não é exposto.
/// </summary>
public sealed record CreateImageAnalysisResult(Guid AnalysisId);
