namespace ImageAnalysis.API.Contracts;

/// <summary>
/// Resposta do POST /analyses: o id para consultar o andamento em GET /analyses/{id}.
/// </summary>
public sealed record CreateAnalysisResponse(Guid Id);
