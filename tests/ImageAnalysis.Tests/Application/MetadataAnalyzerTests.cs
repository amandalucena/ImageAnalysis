using ImageAnalysis.Application;
using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Tests.Application.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace ImageAnalysis.Tests.Application;

public class MetadataAnalyzerTests
{
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0xFF, 0xD9];

    private readonly FakeImageStorage _storage = new();
    private readonly FakeMetadataExtractor _extractor = new();
    private readonly AnalyzerContext _context;

    public MetadataAnalyzerTests()
    {
        var image = new ImageFile("foto.jpg", $"uploads/{Guid.NewGuid()}/original.jpg", ImageBytes.Length, new string('a', 64), "image/jpeg");
        _storage.Files[image.StoragePath] = ImageBytes;
        _context = new AnalyzerContext(Guid.NewGuid(), image);
    }

    private MetadataAnalyzer Analyzer() => new(_storage, _extractor);

    // --- cenários da issue ---

    [Fact]
    public async Task SemMetadata_SoAchadoNeutralDeAusencia()
    {
        _extractor.Json = """[{ "SourceFile": "-" }]""";

        var result = await Analyzer().AnalyzeAsync(_context);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(MetadataFindingNames.MetadataUnavailable, finding.Name);
        Assert.DoesNotContain(result.Findings.SelectMany(f => f.Evidences),
            e => e.Direction == EvidenceDirection.SupportsManipulation);
    }

    [Fact]
    public async Task MetadataCompleta_SemAchados()
    {
        _extractor.Json = Fixture("phone-gps-thumbnail.json");

        var result = await Analyzer().AnalyzeAsync(_context);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task DumpInvalido_LancaMetadataExtractionExceptionToolFailed()
    {
        _extractor.Json = "isto não é json";

        var ex = await Assert.ThrowsAsync<MetadataExtractionException>(() => Analyzer().AnalyzeAsync(_context));

        Assert.Equal(MetadataExtractionFailure.ToolFailed, ex.Failure);
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Fact]
    public async Task ImagemIlegivel_PropagaExcecaoDoExtrator()
    {
        var failure = new MetadataExtractionException(MetadataExtractionFailure.UnreadableImage, "arquivo corrompido");
        _extractor.Exception = failure;

        var ex = await Assert.ThrowsAsync<MetadataExtractionException>(() => Analyzer().AnalyzeAsync(_context));

        Assert.Same(failure, ex);
    }

    [Fact]
    public async Task AvisoDoExifTool_GeraAchadoDeAvisos()
    {
        _extractor.Json = Fixture("jpeg-format-warning.json");

        var result = await Analyzer().AnalyzeAsync(_context);

        Assert.Contains(result.Findings, f => f.Name == MetadataFindingNames.ReadWarnings);
    }

    [Fact]
    public async Task DatasInconsistentes_GeraAchadoDeDatas()
    {
        _extractor.Json = """
            [{
              "IFD0:Make": "Apple",
              "IFD0:Model": "iPhone 13",
              "IFD1:ThumbnailLength": 163,
              "ExifIFD:DateTimeOriginal": "2026:03:01 09:15:44",
              "ExifIFD:CreateDate": "2026:03:01 09:15:44",
              "IFD0:ModifyDate": "2026:02:20 08:00:00"
            }]
            """;

        var result = await Analyzer().AnalyzeAsync(_context);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(MetadataFindingNames.InconsistentDates, finding.Name);
        Assert.Equal(MetadataEvidenceNames.ModifyBeforeCapture, Assert.Single(finding.Evidences).Name);
    }

    // --- orquestração ---

    [Fact]
    public async Task EditadoNoPhotoshop_GeraAchadoDeEditor()
    {
        _extractor.Json = Fixture("edited-photoshop.json");

        var result = await Analyzer().AnalyzeAsync(_context);

        Assert.Contains(result.Findings, f => f.Name == MetadataFindingNames.EditingSoftware);
    }

    [Fact]
    public async Task ExtratorRecebeOsBytesDaImagemDoContexto()
    {
        await Analyzer().AnalyzeAsync(_context);

        Assert.Equal(ImageBytes, _extractor.ReceivedImage);
    }

    [Fact]
    public async Task ImagemAusenteNoStorage_PropagaFileNotFoundSemChamarExtrator()
    {
        _storage.Files.Clear();

        await Assert.ThrowsAsync<FileNotFoundException>(() => Analyzer().AnalyzeAsync(_context));
        Assert.Equal(0, _extractor.Calls);
    }

    [Fact]
    public async Task AnalyzeMetadataAsync_DevolveDumpBrutoNormalizadoEResultado()
    {
        _extractor.Json = Fixture("phone-gps-thumbnail.json");

        var analysis = await Analyzer().AnalyzeMetadataAsync(_context);

        Assert.Equal(_extractor.Json, analysis.Raw.Json);
        Assert.Equal("Apple", analysis.Normalized.Make);
        Assert.Equal(new ImageDimensions(160, 120), analysis.Normalized.Dimensions);
        Assert.Empty(analysis.Result.Findings);
    }

    [Fact]
    public async Task AnalyzeAsync_MesmoResultadoDeAnalyzeMetadataAsync()
    {
        _extractor.Json = Fixture("edited-photoshop.json");

        var full = await Analyzer().AnalyzeMetadataAsync(_context);
        var result = await Analyzer().AnalyzeAsync(_context);

        Assert.Equal(
            full.Result.Findings.Select(f => (f.Name, f.Evidences.Count)),
            result.Findings.Select(f => (f.Name, f.Evidences.Count)));
    }

    [Fact]
    public async Task CancelamentoAntesDeComecar_LancaOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Analyzer().AnalyzeAsync(_context, cts.Token));
    }

    [Fact]
    public void Source_EhMetadata()
    {
        Assert.Equal(SourceFindingType.Metadata, Analyzer().Source);
    }

    [Fact]
    public async Task ContextoNulo_LancaExcecao()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Analyzer().AnalyzeMetadataAsync(null!));
    }

    // --- Evaluate ---

    [Fact]
    public void Evaluate_NenhumaRegraDispara_RetornaEmpty()
    {
        var metadata = new NormalizedMetadata { Make = "Apple", Model = "iPhone 13", HasThumbnail = true };

        Assert.Same(AnalyzerResult.Empty, MetadataFindingRules.Evaluate(metadata));
    }

    [Fact]
    public void Evaluate_AchadosNaOrdemFixa()
    {
        var metadata = new NormalizedMetadata
        {
            Software = [new SoftwareTag("XMP-xmp:CreatorTool", "Adobe Photoshop 25.0 (Firefly)")],
            DateTimeOriginal = new MetadataDate("2026:03:01 09:15:44", new DateTime(2026, 3, 1, 9, 15, 44), null),
            ModifyDate = new MetadataDate("2026:02:01 09:15:44", new DateTime(2026, 2, 1, 9, 15, 44), null),
            Warnings = ["JPEG format error"]
        };

        var names = MetadataFindingRules.Evaluate(metadata).Findings.Select(f => f.Name);

        Assert.Equal(
            [
                MetadataFindingNames.AiGenerator,
                MetadataFindingNames.EditingSoftware,
                MetadataFindingNames.InconsistentDates,
                MetadataFindingNames.StrippedMetadata,
                MetadataFindingNames.ReadWarnings
            ],
            names);
    }

    // --- DI ---

    [Fact]
    public void AddApplication_ConcretoEInterfaceResolvemAMesmaInstanciaNoEscopo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IImageStorage>(_storage);
        services.AddSingleton<IMetadataExtractor>(_extractor);
        services.AddApplication();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var concrete = scope.ServiceProvider.GetRequiredService<MetadataAnalyzer>();
        var analyzers = scope.ServiceProvider.GetServices<IImageAnalyzer>();

        Assert.Same(concrete, Assert.Single(analyzers.OfType<MetadataAnalyzer>()));
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MetadataJson", name));
}
