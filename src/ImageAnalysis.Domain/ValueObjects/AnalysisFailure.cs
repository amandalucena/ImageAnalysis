using ImageAnalysis.Domain.Enums;

namespace ImageAnalysis.Domain.ValueObjects;

/// <summary>
/// Motivo da falha de uma análise. A mensagem deve ser curta e segura para exibição;
/// detalhes técnicos (stack trace, exceção) ficam nos logs, correlacionados pelo AnalysisId.
/// </summary>
public sealed record AnalysisFailure
{
    public AnalysisFailureType Type { get; }
    public string Message { get; }

    public AnalysisFailure(AnalysisFailureType type, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A mensagem da falha é obrigatória.", nameof(message));

        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type), "Tipo de falha inválido.");

        Type = type;
        Message = message;
    }
}
