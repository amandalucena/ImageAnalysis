using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Application.Storage;
using ImageAnalysis.Application.UseCases.ProcessImageAnalysis;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Infrastructure.Persistence.Repositories;
using ImageAnalysis.Tests.Application.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageAnalysis.Tests.Infrastructure.Persistence;

/// <summary>
/// ProcessImageAnalysis gravando no PostgreSQL real. O storage e o ExifTool são simulados com
/// fixtures JSON reais, para que o teste dependa só do banco. Cada execução usa o próprio
/// DbContext, como o Worker faz com um escopo por mensagem, e a releitura usa outro.
/// </summary>
[Collection("Postgres")]
public class ProcessImageAnalysisPersistenceTests
{
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xD9];

    private readonly PostgresFixture _fixture;
    private readonly FakeImageStorage _storage = new();
    private readonly FakeMetadataExtractor _extractor = new();

    public ProcessImageAnalysisPersistenceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExecuteAsync_DumpComSoftwareDeEdicao_PersisteMetadataAchadosEResultado()
    {
        var json = ReadMetadataJson("edited-photoshop.json");
        _extractor.Json = json;
        var analysis = await AddAnalysisAsync();

        var outcome = await ExecuteAsync(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Completed, outcome);

        var loaded = await LoadAsync(analysis.Id);
        Assert.Equal(AnalysisStatus.Completed, loaded.Status);
        Assert.NotNull(loaded.StartedAt);
        Assert.NotNull(loaded.FinishedAt);
        Assert.Null(loaded.Failure);

        Assert.NotNull(loaded.Metadata);
        Assert.Equal(AnalysisStorageKeys.RawMetadata(analysis.Id), loaded.Metadata.RawMetadataPath);
        Assert.Equal(new ImageDimensions(64, 48), loaded.Metadata.Dimensions);

        // O banco não guarda a ordem dos achados: a comparação é por nome.
        var expected = MetadataFindingRules.Evaluate(MetadataNormalizer.Normalize(new RawMetadata(json))).Findings;
        Assert.True(expected.Count > 1, "O fixture deve gerar mais de um achado.");
        Assert.Equal(expected.Select(f => f.Name).Order(), loaded.Findings.Select(f => f.Name).Order());

        foreach (var expectedFinding in expected)
        {
            var finding = Assert.Single(loaded.Findings, f => f.Name == expectedFinding.Name);
            Assert.Equal(expectedFinding.Description, finding.Description);
            Assert.Equal(SourceFindingType.Metadata, finding.SourceFindingType);
            Assert.Equal(EvidenceKeys(expectedFinding.Evidences), EvidenceKeys(finding.Evidences));
        }

        Assert.NotNull(loaded.Result);
        Assert.Equal(AnalysisResultStatus.Inconclusive, loaded.Result.Status);
        Assert.Equal(ConfidenceLevel.Low, loaded.Result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ExtracaoFalha_PersisteFalhaSemMetadataAchadosNemResultado()
    {
        _extractor.Exception = new MetadataExtractionException(MetadataExtractionFailure.ToolFailed, "Exit code 1.");
        var analysis = await AddAnalysisAsync();

        var outcome = await ExecuteAsync(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Failed, outcome);

        var loaded = await LoadAsync(analysis.Id);
        Assert.Equal(AnalysisStatus.Failed, loaded.Status);
        Assert.NotNull(loaded.StartedAt);
        Assert.NotNull(loaded.Failure);
        Assert.Equal(AnalysisFailureType.AnalyzerCrashed, loaded.Failure.Type);
        Assert.Equal("Não foi possível extrair os metadados da imagem.", loaded.Failure.Message);
        Assert.Null(loaded.Metadata);
        Assert.Empty(loaded.Findings);
        Assert.Null(loaded.Result);
    }

    [Fact]
    public async Task ExecuteAsync_DumpSemDimensoes_PersisteInvalidImageFormat()
    {
        _extractor.Json = ReadMetadataJson("jpeg-format-warning.json");
        var analysis = await AddAnalysisAsync();

        await ExecuteAsync(analysis.Id);

        var loaded = await LoadAsync(analysis.Id);
        Assert.Equal(AnalysisStatus.Failed, loaded.Status);
        Assert.Equal(AnalysisFailureType.InvalidImageFormat, loaded.Failure?.Type);
        Assert.Null(loaded.Metadata);
        Assert.Empty(loaded.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_FalhaAoGravarDump_PersisteStorageUnavailable()
    {
        _extractor.Json = ReadMetadataJson("edited-photoshop.json");
        var analysis = await AddAnalysisAsync();
        _storage.Files[AnalysisStorageKeys.RawMetadata(analysis.Id)] = [];

        await ExecuteAsync(analysis.Id);

        var loaded = await LoadAsync(analysis.Id);
        Assert.Equal(AnalysisStatus.Failed, loaded.Status);
        Assert.Equal(AnalysisFailureType.StorageUnavailable, loaded.Failure?.Type);
        Assert.Null(loaded.Metadata);
        Assert.Empty(loaded.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_MensagemDuplicada_NaoDuplicaAchados()
    {
        _extractor.Json = ReadMetadataJson("edited-photoshop.json");
        var analysis = await AddAnalysisAsync();

        await ExecuteAsync(analysis.Id);
        var findingCount = (await LoadAsync(analysis.Id)).Findings.Count;

        var outcome = await ExecuteAsync(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Skipped, outcome);
        Assert.Equal(1, _extractor.Calls);

        var loaded = await LoadAsync(analysis.Id);
        Assert.Equal(AnalysisStatus.Completed, loaded.Status);
        Assert.Equal(findingCount, loaded.Findings.Count);
    }

    private async Task<Analysis> AddAnalysisAsync()
    {
        var image = new ImageFile("foto.jpg", $"uploads/{Guid.NewGuid()}/original.jpg", ImageBytes.Length, new string('a', 64), "image/jpeg");
        var analysis = new Analysis(null, image);
        _storage.Files[image.StoragePath] = ImageBytes;

        await using var dbContext = _fixture.CreateDbContext();
        await new AnalysisRepository(dbContext).AddAsync(analysis);

        return analysis;
    }

    private async Task<ProcessImageAnalysisOutcome> ExecuteAsync(Guid analysisId)
    {
        await using var dbContext = _fixture.CreateDbContext();

        var useCase = new ProcessImageAnalysis(
            new AnalysisRepository(dbContext),
            new MetadataAnalyzer(_storage, _extractor),
            _storage,
            new FakeAnalysisCache(),
            NullLogger<ProcessImageAnalysis>.Instance);

        return await useCase.ExecuteAsync(new ProcessImageAnalysisCommand(analysisId));
    }

    private async Task<Analysis> LoadAsync(Guid analysisId)
    {
        await using var dbContext = _fixture.CreateDbContext();
        return (await new AnalysisRepository(dbContext).GetByIdAsync(analysisId))!;
    }

    private static IEnumerable<(string, string, EvidenceStrength, EvidenceDirection)> EvidenceKeys(IEnumerable<AnalyzerEvidence> evidences) =>
        evidences.Select(e => (e.Name, e.Description, e.Strength, e.Direction)).Order();

    private static IEnumerable<(string, string, EvidenceStrength, EvidenceDirection)> EvidenceKeys(IEnumerable<Evidence> evidences) =>
        evidences.Select(e => (e.Name, e.Description, e.Strength, e.Direction)).Order();

    // Saídas reais do ExifTool (ver MetadataNormalizerTests).
    private static string ReadMetadataJson(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MetadataJson", name));
}
