using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Application.UseCases.GetAnalysisStatus;

/// <summary>
/// Consulta o status e os dados básicos de uma análise. Retorna null se ela não existir.
/// </summary>
public class GetAnalysisStatus
{
    private readonly IAnalysisRepository _analysisRepository;

    public GetAnalysisStatus(IAnalysisRepository analysisRepository)
    {
        _analysisRepository = analysisRepository;
    }

    public async Task<AnalysisStatusResult?> ExecuteAsync(GetAnalysisStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var analysis = await _analysisRepository.GetByIdAsync(query.AnalysisId, cancellationToken);
        if (analysis is null)
            return null;

        return new AnalysisStatusResult(
            analysis.Id,
            analysis.Name,
            analysis.Status.ToString(),
            analysis.Image.OriginalFileName,
            analysis.Image.MimeType,
            analysis.Image.SizeInBytes,
            analysis.Image.Sha256,
            analysis.CreatedAt,
            analysis.StartedAt,
            analysis.FinishedAt);
    }
}
