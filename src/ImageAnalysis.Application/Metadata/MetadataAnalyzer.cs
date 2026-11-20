using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Saída completa da análise de metadados. Além dos achados, leva o dump bruto (gravado em
/// metadata.json para auditoria) e os metadados normalizados (ex.: dimensões para o
/// ImageMetadata), para que quem chama não precise rodar o ExifTool de novo.
/// </summary>
public sealed record MetadataAnalysis(RawMetadata Raw, NormalizedMetadata Normalized, AnalyzerResult Result);

/// <summary>
/// Analisador de metadados: lê a imagem original, extrai o dump bruto, normaliza e aplica as
/// regras de achados. Como todo IImageAnalyzer, não acessa o banco nem grava no storage;
/// persistir o dump e aplicar os achados no agregado é papel da Application (CLAUDE.md 8.1).
/// </summary>
public sealed class MetadataAnalyzer : IImageAnalyzer
{
    private readonly IImageStorage _storage;
    private readonly IMetadataExtractor _extractor;

    public MetadataAnalyzer(IImageStorage storage, IMetadataExtractor extractor)
    {
        _storage = storage;
        _extractor = extractor;
    }

    public SourceFindingType Source => SourceFindingType.Metadata;

    public async Task<AnalyzerResult> AnalyzeAsync(AnalyzerContext context, CancellationToken cancellationToken = default) =>
        (await AnalyzeMetadataAsync(context, cancellationToken)).Result;

    /// <exception cref="FileNotFoundException">A imagem original não está no storage.</exception>
    /// <exception cref="MetadataExtractionException">
    /// A extração falhou ou o dump não está no formato esperado.
    /// </exception>
    public async Task<MetadataAnalysis> AnalyzeMetadataAsync(AnalyzerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        RawMetadata raw;
        await using (var image = await _storage.OpenReadAsync(context.Image.StoragePath, cancellationToken))
        {
            raw = await _extractor.ExtractAsync(image, cancellationToken);
        }

        var normalized = Normalize(raw);

        return new MetadataAnalysis(raw, normalized, MetadataFindingRules.Evaluate(normalized));
    }

    // Um dump fora do formato é falha da ferramenta, não da imagem: vira o mesmo tipo de exceção
    // do extrator, para que quem chama trate as falhas de extração num só lugar.
    private static NormalizedMetadata Normalize(RawMetadata raw)
    {
        try
        {
            return MetadataNormalizer.Normalize(raw);
        }
        catch (ArgumentException ex)
        {
            throw new MetadataExtractionException(
                MetadataExtractionFailure.ToolFailed,
                "O dump de metadados não está no formato esperado.",
                ex);
        }
    }
}
