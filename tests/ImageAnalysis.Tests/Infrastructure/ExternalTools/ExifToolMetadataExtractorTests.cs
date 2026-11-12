using System.Text.Json;
using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Infrastructure.ExternalTools.ExifTool;

namespace ImageAnalysis.Tests.Infrastructure.ExternalTools;

/// <summary>
/// Executa o ExifTool real sobre as imagens de Fixtures/Metadata.
/// </summary>
public class ExifToolMetadataExtractorTests
{
    private readonly ExifToolMetadataExtractor _extractor = new(new ExifToolOptions());

    [ExifToolFact]
    public async Task ExtractAsync_JpegDeCamera_DevolveTagsComGrupoDeOrigem()
    {
        await using var image = OpenFixture("camera.jpg");

        var metadata = await _extractor.ExtractAsync(image);

        var tags = Parse(metadata.Json);
        Assert.Equal("Canon", tags["IFD0:Make"].GetString());
        Assert.Equal("Canon EOS 90D", tags["IFD0:Model"].GetString());
        Assert.Equal("2026:03:01 09:15:44", tags["ExifIFD:DateTimeOriginal"].GetString());
        Assert.Equal(64, tags["File:ImageWidth"].GetInt32());
        Assert.Equal(48, tags["File:ImageHeight"].GetInt32());
    }

    [ExifToolFact]
    public async Task ExtractAsync_TextoComAcentos_PreservaUtf8()
    {
        await using var image = OpenFixture("camera.jpg");

        var metadata = await _extractor.ExtractAsync(image);

        Assert.Equal("Praça da Sé", Parse(metadata.Json)["IFD0:ImageDescription"].GetString());
    }

    [ExifToolFact]
    public async Task ExtractAsync_MesmaTagEmGruposDiferentes_MantemAmbas()
    {
        await using var image = OpenFixture("edited.jpg");

        var metadata = await _extractor.ExtractAsync(image);

        var tags = Parse(metadata.Json);
        Assert.Equal("Adobe Photoshop 25.0 (Windows)", tags["IFD0:Software"].GetString());
        Assert.Equal("Adobe Photoshop 25.0 (Windows)", tags["XMP-xmp:CreatorTool"].GetString());
    }

    [ExifToolFact]
    public async Task ExtractAsync_LendoDoStdin_NaoIncluiGrupoSystem()
    {
        await using var image = OpenFixture("camera.jpg");

        var metadata = await _extractor.ExtractAsync(image);

        Assert.DoesNotContain(Parse(metadata.Json).Keys, key => key.StartsWith("System:"));
    }

    [ExifToolFact]
    public async Task ExtractAsync_JpegSemMetadados_NaoEFalha()
    {
        await using var image = OpenFixture("no-metadata.jpg");

        var metadata = await _extractor.ExtractAsync(image);

        var tags = Parse(metadata.Json);
        Assert.Equal("JPEG", tags["File:FileType"].GetString());
        Assert.False(tags.ContainsKey("IFD0:Make"));
    }

    [ExifToolFact]
    public async Task ExtractAsync_Png_DevolveTipoEDimensoes()
    {
        await using var image = OpenFixture("no-metadata.png");

        var metadata = await _extractor.ExtractAsync(image);

        var tags = Parse(metadata.Json);
        Assert.Equal("PNG", tags["File:FileType"].GetString());
        Assert.Equal(64, tags["PNG:ImageWidth"].GetInt32());
    }

    [ExifToolFact]
    public async Task ExtractAsync_NaoFechaOStreamRecebido()
    {
        await using var image = OpenFixture("camera.jpg");

        await _extractor.ExtractAsync(image);

        Assert.True(image.CanRead);
    }

    [ExifToolFact]
    public async Task ExtractAsync_JpegCorrompido_DevolveDumpComAviso()
    {
        // Marcador de início de JPEG seguido de bytes sem estrutura.
        var content = new byte[] { 0xFF, 0xD8, 0xFF }.Concat(RandomBytes(500, seed: 7)).ToArray();

        var metadata = await _extractor.ExtractAsync(new MemoryStream(content));

        var tags = Parse(metadata.Json);
        Assert.Equal("JPEG", tags["File:FileType"].GetString());
        Assert.True(tags.ContainsKey("ExifTool:Warning"));
    }

    [ExifToolFact]
    public async Task ExtractAsync_BytesQueNaoSaoImagem_LancaUnreadableImage()
    {
        var ex = await Assert.ThrowsAsync<MetadataExtractionException>(
            () => _extractor.ExtractAsync(new MemoryStream(RandomBytes(2000, seed: 42))));

        Assert.Equal(MetadataExtractionFailure.UnreadableImage, ex.Failure);
    }

    [ExifToolFact]
    public async Task ExtractAsync_ArquivoGrandeDeTipoDesconhecido_LancaUnreadableImageSemErroDePipe()
    {
        // O ExifTool desiste no cabeçalho e fecha o stdin com quase tudo ainda por enviar.
        var ex = await Assert.ThrowsAsync<MetadataExtractionException>(
            () => _extractor.ExtractAsync(new MemoryStream(RandomBytes(8 * 1024 * 1024, seed: 42))));

        Assert.Equal(MetadataExtractionFailure.UnreadableImage, ex.Failure);
    }

    [ExifToolFact]
    public async Task ExtractAsync_StreamVazio_LancaUnreadableImage()
    {
        var ex = await Assert.ThrowsAsync<MetadataExtractionException>(
            () => _extractor.ExtractAsync(new MemoryStream()));

        Assert.Equal(MetadataExtractionFailure.UnreadableImage, ex.Failure);
    }

    [ExifToolFact]
    public async Task ExtractAsync_TempoLimiteExcedido_LancaTimeout()
    {
        var extractor = new ExifToolMetadataExtractor(new ExifToolOptions { Timeout = TimeSpan.FromMilliseconds(1) });
        await using var image = OpenFixture("camera.jpg");

        var ex = await Assert.ThrowsAsync<MetadataExtractionException>(() => extractor.ExtractAsync(image));

        Assert.Equal(MetadataExtractionFailure.Timeout, ex.Failure);
    }

    [ExifToolFact]
    public async Task ExtractAsync_FalhaAoLerAImagem_PropagaErroDeLeitura()
    {
        // Um erro do storage não pode virar um dump de parte do arquivo.
        await Assert.ThrowsAsync<IOException>(() => _extractor.ExtractAsync(new FailingStream()));
    }

    [Fact]
    public async Task ExtractAsync_ExecutavelInexistente_LancaToolUnavailable()
    {
        var extractor = new ExifToolMetadataExtractor(
            new ExifToolOptions { ExecutablePath = $"exiftool-inexistente-{Guid.NewGuid():N}" });

        var ex = await Assert.ThrowsAsync<MetadataExtractionException>(
            () => extractor.ExtractAsync(new MemoryStream([1, 2, 3])));

        Assert.Equal(MetadataExtractionFailure.ToolUnavailable, ex.Failure);
    }

    [Fact]
    public async Task ExtractAsync_CancelamentoDeQuemChama_LancaOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _extractor.ExtractAsync(new MemoryStream([1, 2, 3]), cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Construtor_CaminhoVazio_LancaExcecao(string path)
    {
        Assert.Throws<ArgumentException>(() => new ExifToolMetadataExtractor(new ExifToolOptions { ExecutablePath = path }));
    }

    [Fact]
    public void Construtor_TempoLimiteZero_LancaExcecao()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExifToolMetadataExtractor(new ExifToolOptions { Timeout = TimeSpan.Zero }));
    }

    private static FileStream OpenFixture(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", name));

    private static Dictionary<string, JsonElement> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement[0].EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Falha simulada na leitura do storage.");
    }
}
