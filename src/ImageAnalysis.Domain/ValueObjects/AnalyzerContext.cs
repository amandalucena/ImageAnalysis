namespace ImageAnalysis.Domain.ValueObjects;

/// <summary>
/// Entrada de um analisador: a análise em execução e a imagem original no storage.
/// Leva só dados, não a entidade Analysis, para que o analisador não altere o agregado.
/// </summary>
public sealed record AnalyzerContext
{
    public Guid AnalysisId { get; }
    public ImageFile Image { get; }

    public AnalyzerContext(Guid analysisId, ImageFile image)
    {
        if (analysisId == Guid.Empty)
            throw new ArgumentException("O identificador da análise é obrigatório.", nameof(analysisId));

        ArgumentNullException.ThrowIfNull(image);

        AnalysisId = analysisId;
        Image = image;
    }
}
