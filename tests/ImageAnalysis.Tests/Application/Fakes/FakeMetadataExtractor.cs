using ImageAnalysis.Application.Metadata;

namespace ImageAnalysis.Tests.Application.Fakes;

// Devolve o JSON configurado (ou lança a exceção configurada) e guarda os bytes recebidos,
// para conferir que o analisador leu a imagem certa.
public class FakeMetadataExtractor : IMetadataExtractor
{
    public string Json { get; set; } = """[{ "SourceFile": "-" }]""";
    public Exception? Exception { get; set; }

    public byte[]? ReceivedImage { get; private set; }
    public int Calls { get; private set; }

    public async Task<RawMetadata> ExtractAsync(Stream image, CancellationToken cancellationToken = default)
    {
        Calls++;

        using var copy = new MemoryStream();
        await image.CopyToAsync(copy, cancellationToken);
        ReceivedImage = copy.ToArray();

        if (Exception is not null)
            throw Exception;

        return new RawMetadata(Json);
    }
}
