namespace ImageAnalysis.Domain.Enums;

public enum AnalysisFailureType
{
    InvalidImageFormat = 0,
    StorageUnavailable = 1,
    AnalyzerCrashed = 2,

    // A análise foi gravada, mas não pôde ser enviada para a fila de processamento.
    QueueUnavailable = 3
}
