namespace ImageAnalysis.Domain.Exceptions;

/// <summary>
/// A análise foi alterada por outra operação (ex.: outra instância do Worker ou uma
/// mensagem duplicada) entre a leitura e a gravação. Quem recebe decide se descarta
/// a operação ou recarrega a análise e tenta de novo. As alterações não gravadas são
/// descartadas, então o retry deve partir de uma nova chamada a GetByIdAsync.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public Guid AnalysisId { get; }

    public ConcurrencyConflictException(Guid analysisId, Exception? innerException = null)
        : base($"A análise {analysisId} foi alterada por outra operação.", innerException)
    {
        AnalysisId = analysisId;
    }
}
