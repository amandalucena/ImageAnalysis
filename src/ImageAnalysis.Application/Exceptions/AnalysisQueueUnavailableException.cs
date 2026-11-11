namespace ImageAnalysis.Application.Exceptions;

/// <summary>
/// A análise foi criada, mas não pôde ser enviada para processamento. Ela já foi
/// marcada como Failed (QueueUnavailable), então não fica presa em Pending.
/// </summary>
public class AnalysisQueueUnavailableException : Exception
{
    public Guid AnalysisId { get; }

    public AnalysisQueueUnavailableException(Guid analysisId, Exception innerException)
        : base($"A análise {analysisId} não pôde ser enviada para processamento.", innerException)
    {
        AnalysisId = analysisId;
    }
}
