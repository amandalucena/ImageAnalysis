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
    private readonly FakeAnalysisRepository _repository = new();

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
    public async Task ExecuteAsync_SemAnalisadores_ConcluiComResultadoInconclusivoDeBaixaConfianca()
    {
        var analysis = AddAnalysis();

        await Execute(analysis.Id);

        Assert.NotNull(analysis.Result);
        Assert.Equal(AnalysisResultStatus.Inconclusive, analysis.Result.Status);
        Assert.Equal(ConfidenceLevel.Low, analysis.Result.Confidence);
        Assert.NotNull(analysis.Result.Limitations);
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
        return analysis;
    }

    private Task<ProcessImageAnalysisOutcome> Execute(Guid analysisId) =>
        new ProcessImageAnalysis(_repository, NullLogger<ProcessImageAnalysis>.Instance)
            .ExecuteAsync(new ProcessImageAnalysisCommand(analysisId));
}
