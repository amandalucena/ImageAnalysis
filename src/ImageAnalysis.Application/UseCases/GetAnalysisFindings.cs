using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Application.UseCases.GetAnalysisFindings;

// Arquivo único com a consulta, os DTOs e o caso de uso, em vez da pasta com um arquivo por tipo
// usada pelos outros casos de uso.

/// <summary>
/// Entrada do GetAnalysisFindings: o id devolvido na criação da análise.
/// </summary>
public sealed record GetAnalysisFindingsQuery(Guid AnalysisId);

/// <summary>
/// Saída do GetAnalysisFindings. O status permite ao cliente saber se a lista já é definitiva:
/// ela fica vazia enquanto a análise está em Pending ou Processing, e também em Failed, porque
/// uma análise que falha não grava achados.
/// </summary>
public sealed record AnalysisFindingsResult(Guid AnalysisId, string Status, IReadOnlyList<FindingResult> Findings);

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

/// <summary>
/// Evidência que sustenta um achado.
/// </summary>
public sealed record EvidenceResult(string Name, string Description, string Strength, string Direction);

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
        var findings = analysis.Findings
            .OrderBy(f => f.SourceFindingType)
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
