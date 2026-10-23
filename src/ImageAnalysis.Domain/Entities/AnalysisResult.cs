using ImageAnalysis.Domain.Entities.Common;
using ImageAnalysis.Domain.Enums;

namespace ImageAnalysis.Domain.Entities;

/// <summary>
/// Conclusão consolidada de uma análise, produzida pelo Evidence Aggregator.
/// Uma Analysis concluída possui um único AnalysisResult (1:1).
/// </summary>
public class AnalysisResult : BaseEntity
{
    public Guid AnalysisId { get; private set; }
    public AnalysisResultStatus Status { get; private set; }
    public ConfidenceLevel Confidence { get; private set; }
    public string Summary { get; private set; } = null!;

    // Limitações da análise (ex.: ELA pouco confiável em imagens muito recomprimidas).
    public string? Limitations { get; private set; }

    // Construtor para materialização pelo ORM.
    private AnalysisResult()
    {
    }

    internal AnalysisResult(Guid analysisId, AnalysisResultStatus status, ConfidenceLevel confidence, string summary, string? limitations = null)
    {
        if (string.IsNullOrWhiteSpace(summary))
            throw new ArgumentException("O resumo do resultado é obrigatório.", nameof(summary));

        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), "Status do resultado inválido.");

        if (!Enum.IsDefined(confidence))
            throw new ArgumentOutOfRangeException(nameof(confidence), "Nível de confiança inválido.");

        AnalysisId = analysisId;
        Status = status;
        Confidence = confidence;
        Summary = summary;
        Limitations = limitations;
    }
}
