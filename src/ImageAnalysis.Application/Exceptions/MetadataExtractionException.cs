namespace ImageAnalysis.Application.Exceptions;

public enum MetadataExtractionFailure
{
    // Executável não encontrado ou sem permissão de execução.
    ToolUnavailable = 0,

    Timeout = 1,

    // A ferramenta rodou, mas não conseguiu ler o arquivo (vazio, tipo desconhecido, corrompido).
    UnreadableImage = 2,

    // Exit code ou saída inesperados.
    ToolFailed = 3
}

/// <summary>
/// A extração de metadados não produziu um dump. A mensagem é técnica e vai para o log;
/// não deve ser exibida ao usuário.
/// </summary>
public class MetadataExtractionException : Exception
{
    public MetadataExtractionFailure Failure { get; }

    public MetadataExtractionException(MetadataExtractionFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }
}
