namespace ImageAnalysis.Domain.Enums;

/// <summary>
/// Conclusão consolidada da análise. Nomenclatura provisória,
/// a ser revisada após a validação dos algoritmos.
/// </summary>
public enum AnalysisResultStatus
{
    NoRelevantEvidence = 0,
    Inconclusive = 1,
    RequiresFurtherInvestigation = 2
}
