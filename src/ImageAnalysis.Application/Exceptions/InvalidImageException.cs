namespace ImageAnalysis.Application.Exceptions;

/// <summary>
/// O arquivo enviado não pode ser aceito para análise (vazio, sem nome ou de tipo não suportado).
/// A mensagem é segura para exibição ao usuário.
/// </summary>
public class InvalidImageException : Exception
{
    public InvalidImageException(string message)
        : base(message)
    {
    }
}
