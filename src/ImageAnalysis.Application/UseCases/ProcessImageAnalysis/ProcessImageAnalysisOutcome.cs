namespace ImageAnalysis.Application.UseCases.ProcessImageAnalysis;

/// <summary>
/// O que aconteceu com a mensagem. Em todos os casos ela foi tratada e pode ser
/// removida da fila.
/// </summary>
public enum ProcessImageAnalysisOutcome
{
    Completed = 0,
    Failed = 1,

    // A análise já tinha saído de Pending (mensagem duplicada ou outra execução).
    Skipped = 2,

    // Não existe análise com o id da mensagem.
    NotFound = 3
}
