using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Application.UseCases.GetAnalysisFindings;

/// <summary>
/// Consulta os achados de uma análise com as suas evidências. Retorna null se ela não existir.
/// </summary>
public class GetAnalysisFindings
{
    private readonly IAnalysisRepository _analysisRepository;

    public GetAnalysisFindings(IAnalysisRepository analysisRepository)
    {
        _analysisRepository = analysisRepository;
    }

    public async Task<AnalysisFindingsResult?> ExecuteAsync(GetAnalysisFindingsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var analysis = await _analysisRepository.GetByIdAsync(query.AnalysisId, cancellationToken);
        if (analysis is null)
            return null;

        // O banco não guarda a ordem de criação, e o CreatedAt empata entre achados criados no
        // mesmo processamento: a ordenação explícita faz a mesma análise sempre sair igual.
        // A origem é ordenada pelo texto que o cliente vê, e não pelo número do enum, para que
        // reordenar o SourceFindingType não mude a resposta.
        var findings = analysis.Findings
            .OrderBy(f => f.SourceFindingType.ToString(), StringComparer.Ordinal)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .Select(ToResult)
            .ToArray();

        return new AnalysisFindingsResult(analysis.Id, analysis.Status.ToString(), findings);
    }

    private static FindingResult ToResult(Finding finding) => new(
        finding.Id,
        finding.Name,
        finding.SourceFindingType.ToString(),
        finding.Description,
        finding.Evidences
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.Description, StringComparer.Ordinal)
            .Select(e => new EvidenceResult(e.Name, e.Description, e.Strength.ToString(), e.Direction.ToString()))
            .ToArray());
}
