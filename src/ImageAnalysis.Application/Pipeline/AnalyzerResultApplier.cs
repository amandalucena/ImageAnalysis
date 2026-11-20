using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Pipeline;

/// <summary>
/// Transforma a saída de um analisador em Findings e Evidences da Analysis. Os analisadores só
/// produzem dados; criar as entidades pela raiz do agregado é papel da Application
/// (CLAUDE.md 8.1). Também será usado pela orquestração de vários analisadores (#48).
/// </summary>
public static class AnalyzerResultApplier
{
    public static void Apply(Analysis analysis, SourceFindingType source, AnalyzerResult result)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(result);

        foreach (var analyzerFinding in result.Findings)
        {
            var finding = analysis.AddFinding(analyzerFinding.Name, source, analyzerFinding.Description);

            foreach (var evidence in analyzerFinding.Evidences)
            {
                analysis.AddEvidence(finding.Id, evidence.Name, evidence.Description, evidence.Strength, evidence.Direction);
            }
        }
    }
}
