using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Metadata;
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
    private readonly FakeMetadataExtractor _extractor = new();

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

    private ProcessImageAnalysis CreateUseCase() =>
        new(_repository, new MetadataAnalyzer(_storage, _extractor), NullLogger<ProcessImageAnalysis>.Instance);

    private Task<ProcessImageAnalysisOutcome> Execute(Guid analysisId) =>
        CreateUseCase().ExecuteAsync(new ProcessImageAnalysisCommand(analysisId));
}
