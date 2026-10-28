namespace ImageAnalysis.Application.Exceptions;

/// <summary>
/// O envio não pode ser aceito para análise (arquivo vazio, sem nome, de tipo não suportado
/// ou com nome acima do limite).
/// A mensagem é segura para exibição ao usuário.
/// </summary>
public class InvalidImageException : Exception
{
    public InvalidImageException(string message)
        : base(message)
    {
    }
}
