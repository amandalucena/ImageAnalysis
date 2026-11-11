namespace ImageAnalysis.Domain.Interfaces;

/// <summary>
/// Fila de análises a processar. Quem publica só informa o id: o Worker carrega a
/// Analysis do repositório ao consumir a mensagem.
/// </summary>
public interface IAnalysisQueue
{
    // Só retorna depois que a mensagem foi aceita pela fila; lança exceção se não foi.
    Task PublishAsync(Guid analysisId, CancellationToken cancellationToken = default);
}
