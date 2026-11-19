using ImageAnalysis.Application.Exceptions;
using System.Text;
using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Application.Storage;
using ImageAnalysis.Application.UseCases.ProcessImageAnalysis;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Exceptions;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Tests.Application.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageAnalysis.Tests.Application;

public class ProcessImageAnalysisTests
{
    private static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0xFF, 0xD9];

    private readonly FakeAnalysisRepository _repository = new();
    private readonly FakeImageStorage _storage = new();
    private readonly FakeMetadataExtractor _extractor = new() { Json = MetadataJson };

    // Dump mínimo com as dimensões que o SetMetadata exige.
    private const string MetadataJson = """[{ "SourceFile": "-", "File:ImageWidth": 800, "File:ImageHeight": 600 }]""";

    [Fact]
    public async Task ExecuteAsync_AnalisePendente_GravaProcessingEDepoisCompleted()
    {
        var analysis = AddAnalysis();

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Completed, outcome);
        Assert.Equal([AnalysisStatus.Processing, AnalysisStatus.Completed], _repository.SavedStatuses);
        Assert.NotNull(analysis.StartedAt);
        Assert.NotNull(analysis.FinishedAt);
        Assert.Null(analysis.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_SemConsolidacaoDosAchados_ConcluiComResultadoInconclusivoDeBaixaConfianca()
    {
        var analysis = AddAnalysis();

        await Execute(analysis.Id);

        Assert.NotNull(analysis.Result);
        Assert.Equal(AnalysisResultStatus.Inconclusive, analysis.Result.Status);
        Assert.Equal(ConfidenceLevel.Low, analysis.Result.Confidence);
        Assert.NotNull(analysis.Result.Limitations);
    }

    [Fact]
    public async Task ExecuteAsync_GravacaoFinalNaoPodeSerCancelada()
    {
        var analysis = AddAnalysis();
        using var stopping = new CancellationTokenSource();

        await CreateUseCase()
            .ExecuteAsync(new ProcessImageAnalysisCommand(analysis.Id), stopping.Token);

        Assert.Equal(stopping.Token, _repository.UpdateCancellationTokens[0]);
        Assert.False(_repository.UpdateCancellationTokens[1].CanBeCanceled);
    }

    [Fact]
    public async Task ExecuteAsync_AnalisePendente_ExtraiMetadadosDaImagemOriginalUmaVez()
    {
        var analysis = AddAnalysis();

        await Execute(analysis.Id);

        Assert.Equal(1, _extractor.Calls);
        Assert.Equal(ImageBytes, _extractor.ReceivedImage);
    }

    [Fact]
    public async Task ExecuteAsync_ExtracaoFalha_GravaFailedComAnalyzerCrashed()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new MetadataExtractionException(MetadataExtractionFailure.ToolFailed, "Exit code 1.");

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Failed, outcome);
        Assert.Equal([AnalysisStatus.Processing, AnalysisStatus.Failed], _repository.SavedStatuses);
        Assert.Equal(AnalysisFailureType.AnalyzerCrashed, analysis.Failure?.Type);
        Assert.Null(analysis.Result);
    }

    [Fact]
    public async Task ExecuteAsync_ImagemAusenteNoStorage_GravaFailed()
    {
        var analysis = AddAnalysis();
        _storage.Files.Clear();

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Failed, outcome);
        Assert.Equal(AnalysisStatus.Failed, analysis.Status);
        Assert.Equal(0, _extractor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_AnalisePendente_GravaDumpNoStorageSemAlteracoes()
    {
        var analysis = AddAnalysis();

        await Execute(analysis.Id);

        var saved = _storage.Files[AnalysisStorageKeys.RawMetadata(analysis.Id)];
        Assert.Equal(Encoding.UTF8.GetBytes(MetadataJson), saved);
        Assert.False(saved.AsSpan().StartsWith(Encoding.UTF8.Preamble), "O metadata.json não deve ter BOM.");
    }

    [Fact]
    public async Task ExecuteAsync_AnalisePendente_DefineMetadataComCaminhoDoDumpEDimensoes()
    {
        var analysis = AddAnalysis();

        await Execute(analysis.Id);

        Assert.NotNull(analysis.Metadata);
        Assert.Equal(AnalysisStorageKeys.RawMetadata(analysis.Id), analysis.Metadata.RawMetadataPath);
        Assert.Equal(new ImageDimensions(800, 600), analysis.Metadata.Dimensions);
    }

    [Fact]
    public async Task ExecuteAsync_AnalisePendente_MetadataEntraNaGravacaoFinal()
    {
        var analysis = AddAnalysis();

        await Execute(analysis.Id);

        Assert.Null(_repository.SavedMetadata[0]);
        Assert.Same(analysis.Metadata, _repository.SavedMetadata[1]);
    }

    [Fact]
    public async Task ExecuteAsync_FalhaAoGravarDump_GravaFailedSemMetadata()
    {
        var analysis = AddAnalysis();
        _storage.Files[AnalysisStorageKeys.RawMetadata(analysis.Id)] = [];

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Failed, outcome);
        Assert.Equal(AnalysisStatus.Failed, analysis.Status);
        Assert.Null(analysis.Metadata);
    }

    [Fact]
    public async Task ExecuteAsync_ExtracaoFalha_NaoGravaDumpNemMetadata()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new MetadataExtractionException(MetadataExtractionFailure.ToolFailed, "Exit code 1.");

        await Execute(analysis.Id);

        Assert.False(_storage.Files.ContainsKey(AnalysisStorageKeys.RawMetadata(analysis.Id)));
        Assert.Null(analysis.Metadata);
    }

    [Fact]
    public async Task ExecuteAsync_DumpSemDimensoes_GravaFailedComInvalidImageFormat()
    {
        var analysis = AddAnalysis();
        _extractor.Json = ReadMetadataJson("jpeg-format-warning.json");

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Failed, outcome);
        Assert.Equal([AnalysisStatus.Processing, AnalysisStatus.Failed], _repository.SavedStatuses);
        Assert.Equal(AnalysisFailureType.InvalidImageFormat, analysis.Failure?.Type);
        Assert.Null(analysis.Result);
        Assert.Null(analysis.Metadata);
        Assert.False(_storage.Files.ContainsKey(AnalysisStorageKeys.RawMetadata(analysis.Id)));
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseInexistente_RetornaNotFoundSemGravar()
    {
        var outcome = await Execute(Guid.NewGuid());

        Assert.Equal(ProcessImageAnalysisOutcome.NotFound, outcome);
        Assert.Empty(_repository.SavedStatuses);
    }

    [Fact]
    public async Task ExecuteAsync_MensagemDuplicadaDeAnaliseConcluida_IgnoraSemGravar()
    {
        var analysis = AddAnalysis();
        await Execute(analysis.Id);
        _repository.SavedStatuses.Clear();

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Skipped, outcome);
        Assert.Empty(_repository.SavedStatuses);
        Assert.Equal(AnalysisStatus.Completed, analysis.Status);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseEmProcessamento_IgnoraSemGravar()
    {
        var analysis = AddAnalysis();
        analysis.Start();

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Skipped, outcome);
        Assert.Empty(_repository.SavedStatuses);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseFalhaAntesDeComecar_IgnoraSemGravar()
    {
        var analysis = AddAnalysis();
        analysis.Fail(new AnalysisFailure(AnalysisFailureType.QueueUnavailable, "Falha ao publicar."));

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Skipped, outcome);
        Assert.Equal(AnalysisStatus.Failed, analysis.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ConflitoAoIniciar_IgnoraPorqueOutraExecucaoJaIniciou()
    {
        var analysis = AddAnalysis();
        _repository.UpdateException = new ConcurrencyConflictException(analysis.Id);

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Skipped, outcome);
    }

    [Fact]
    public async Task ExecuteAsync_ErroAoGravar_Propaga()
    {
        var analysis = AddAnalysis();
        _repository.UpdateException = new InvalidOperationException("Banco indisponível.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(analysis.Id));
    }

    private Analysis AddAnalysis()
    {
        var image = new ImageFile("foto.jpg", $"uploads/{Guid.NewGuid()}/original.jpg", 1_024, new string('a', 64), "image/jpeg");
        var analysis = new Analysis(null, image);
        _repository.Analyses.Add(analysis);
        _storage.Files[image.StoragePath] = ImageBytes;
        return analysis;
    }

    // Saídas reais do ExifTool (ver MetadataNormalizerTests).
    private static string ReadMetadataJson(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MetadataJson", name));

    private ProcessImageAnalysis CreateUseCase() =>
        new(_repository, new MetadataAnalyzer(_storage, _extractor), _storage, NullLogger<ProcessImageAnalysis>.Instance);

    private Task<ProcessImageAnalysisOutcome> Execute(Guid analysisId) =>
        CreateUseCase().ExecuteAsync(new ProcessImageAnalysisCommand(analysisId));
}
