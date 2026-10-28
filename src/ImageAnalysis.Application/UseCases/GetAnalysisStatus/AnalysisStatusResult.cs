namespace ImageAnalysis.Application.UseCases.GetAnalysisStatus;

/// <summary>
/// Saída do GetAnalysisStatus: status e dados básicos da análise.
/// O caminho da imagem no storage é um detalhe interno e não é exposto; o SHA-256 é,
/// para que o usuário possa conferir o arquivo que enviou.
/// </summary>
public sealed record AnalysisStatusResult(
    Guid Id,
    string? Name,
    string Status,
    string OriginalFileName,
    string MimeType,
    long SizeInBytes,
    string Sha256,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt);
