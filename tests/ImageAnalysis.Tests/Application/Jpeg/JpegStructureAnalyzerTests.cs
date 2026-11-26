using ImageAnalysis.Application;
using ImageAnalysis.Application.Jpeg;
using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Tests.Application.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Tests.Application.Jpeg;

public class JpegStructureAnalyzerTests
{
    private readonly FakeImageStorage _storage = new();

    private JpegStructureAnalyzer Analyzer() => new(_storage);

    [Fact]
    public void Source_EhJpegStructure()
    {
        Assert.Equal(SourceFindingType.JpegStructure, Analyzer().Source);
    }

    [Fact]
    public async Task JpegReal_GeraOPerfilDeCodificacao()
    {
        var context = Store(Fixture("camera.jpg"), "image/jpeg");

        var result = await Analyzer().AnalyzeAsync(context);

        Assert.Equal(JpegStructureFindingNames.EncodingProfile, Assert.Single(result.Findings).Name);
    }

    [Fact]
    public async Task JpegTruncado_GeraAchadoDeTruncamento()
    {
        var bytes = Fixture("camera.jpg");
        var context = Store(bytes[..(bytes.Length - 10)], "image/jpeg");

        var result = await Analyzer().AnalyzeAsync(context);

        Assert.Contains(result.Findings, f => f.Name == JpegStructureFindingNames.Truncated);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    public async Task ImagemQueNaoEhJpeg_DevolveVazioSemAbrirOArquivo(string mimeType)
    {
        // Sem o arquivo no storage: se o analisador tentasse abrir, lançaria FileNotFoundException.
        var context = Context(mimeType);

        var result = await Analyzer().AnalyzeAsync(context);

        Assert.Same(AnalyzerResult.Empty, result);
    }

    [Fact]
    public async Task MimeEmMaiusculas_TrataComoJpeg()
    {
        var context = Store(Fixture("camera.jpg"), "IMAGE/JPEG");

        var result = await Analyzer().AnalyzeAsync(context);

        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public async Task ImagemAusenteNoStorage_LancaFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => Analyzer().AnalyzeAsync(Context("image/jpeg")));
    }

    [Fact]
    public async Task TokenCancelado_CancelaALeitura()
    {
        var context = Store(Fixture("camera.jpg"), "image/jpeg");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Analyzer().AnalyzeAsync(context, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ContextoNulo_LancaExcecao()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Analyzer().AnalyzeAsync(null!));
    }

    // --- DI ---

    [Fact]
    public void AddApplication_RegistraOsDoisAnalisadoresComAMesmaInstanciaDoConcreto()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IImageStorage>(_storage);
        services.AddSingleton<IMetadataExtractor>(new FakeMetadataExtractor());
        services.AddApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var concrete = scope.ServiceProvider.GetRequiredService<JpegStructureAnalyzer>();
        var analyzers = scope.ServiceProvider.GetServices<IImageAnalyzer>().ToArray();

        Assert.Same(concrete, Assert.Single(analyzers.OfType<JpegStructureAnalyzer>()));
        Assert.Single(analyzers.OfType<MetadataAnalyzer>());
        Assert.Equal(2, analyzers.Length);
    }

    private AnalyzerContext Store(byte[] bytes, string mimeType)
    {
        var context = Context(mimeType, bytes.Length);
        _storage.Files[context.Image.StoragePath] = bytes;
        return context;
    }

    private static AnalyzerContext Context(string mimeType, long size = 100) =>
        new(Guid.NewGuid(), new ImageFile("foto.jpg", $"uploads/{Guid.NewGuid()}/original", size, new string('a', 64), mimeType));

    private static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", fileName));
}
