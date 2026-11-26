using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.Jpeg;

/// <summary>
/// Analisador da estrutura JPEG: lê a imagem original, localiza e interpreta os segmentos e
/// aplica as regras de achados. Como todo IImageAnalyzer, não acessa o banco nem altera a
/// Analysis; aplicar os achados no agregado é papel da Application (CLAUDE.md 8.1).
/// O parser é puro e rápido, então quem precisar da estrutura (ex.: o CompressionAnalyzer, #23)
/// pode chamá-lo direto em vez de recebê-la daqui.
/// </summary>
public sealed class JpegStructureAnalyzer : IImageAnalyzer
{
    public const string JpegMimeType = "image/jpeg";

    private readonly IImageStorage _storage;

    public JpegStructureAnalyzer(IImageStorage storage)
    {
        _storage = storage;
    }

    public SourceFindingType Source => SourceFindingType.JpegStructure;

    /// <summary>
    /// Uma imagem que não é JPEG devolve AnalyzerResult.Empty sem abrir o arquivo: o MIME foi
    /// detectado pelo conteúdo no upload, e a ausência da estrutura JPEG não é evidência.
    /// </summary>
    /// <exception cref="FileNotFoundException">A imagem original não está no storage.</exception>
    public async Task<AnalyzerResult> AnalyzeAsync(AnalyzerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.Equals(context.Image.MimeType, JpegMimeType, StringComparison.OrdinalIgnoreCase))
            return AnalyzerResult.Empty;

        var bytes = await ReadImageAsync(context.Image, cancellationToken);

        return JpegStructureFindingRules.Evaluate(JpegStructureParser.Parse(bytes));
    }

    // O arquivo inteiro vai para a memória: o upload é limitado a 20 MB, e o parser lê os
    // segmentos pelos offsets.
    private async Task<byte[]> ReadImageAsync(ImageFile image, CancellationToken cancellationToken)
    {
        await using var stream = await _storage.OpenReadAsync(image.StoragePath, cancellationToken);

        using var buffer = new MemoryStream((int)Math.Min(image.SizeInBytes, int.MaxValue));
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
