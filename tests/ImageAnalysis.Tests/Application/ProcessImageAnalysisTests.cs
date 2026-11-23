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
    private readonly FakeAnalysisCache _cache = new();
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
        Assert.Equal(AnalysisFailureType.StorageUnavailable, analysis.Failure?.Type);
        Assert.Equal("A imagem original não foi encontrada no armazenamento.", analysis.Failure?.Message);
        Assert.Equal(0, _extractor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ErroDeLeituraNoStorage_GravaStorageUnavailableComMensagemGenerica()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new IOException("Disco indisponível.");

        await Execute(analysis.Id);

        Assert.Equal(AnalysisFailureType.StorageUnavailable, analysis.Failure?.Type);
        Assert.Equal("Não foi possível acessar o armazenamento de arquivos.", analysis.Failure?.Message);
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
        Assert.Equal(AnalysisFailureType.StorageUnavailable, analysis.Failure?.Type);
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
    public async Task ExecuteAsync_DumpComSoftwareDeEdicao_GravaAchadoComEvidencias()
    {
        var analysis = AddAnalysis();
        _extractor.Json = ReadMetadataJson("edited-photoshop.json");

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Completed, outcome);
        var finding = Assert.Single(analysis.Findings, f => f.Name == MetadataFindingNames.EditingSoftware);
        Assert.Equal(SourceFindingType.Metadata, finding.SourceFindingType);
        Assert.Contains(finding.Evidences, e =>
            e.Strength == EvidenceStrength.Medium
            && e.Direction == EvidenceDirection.SupportsManipulation
            && e.Description.Contains("Adobe Photoshop"));
    }

    [Fact]
    public async Task ExecuteAsync_DumpDeCameraSemSinais_ConcluiSemAchados()
    {
        var analysis = AddAnalysis();
        _extractor.Json = ReadMetadataJson("phone-gps-thumbnail.json");

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Completed, outcome);
        Assert.Empty(analysis.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_AchadosEntramSoNaGravacaoFinal()
    {
        var analysis = AddAnalysis();
        _extractor.Json = ReadMetadataJson("edited-photoshop.json");

        await Execute(analysis.Id);

        Assert.Equal(0, _repository.SavedFindingCounts[0]);
        Assert.Equal(analysis.Findings.Count, _repository.SavedFindingCounts[1]);
        Assert.NotEmpty(analysis.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ExtracaoFalha_NaoGravaAchados()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new MetadataExtractionException(MetadataExtractionFailure.ToolFailed, "Exit code 1.");

        await Execute(analysis.Id);

        Assert.Equal(AnalysisStatus.Failed, analysis.Status);
        Assert.Empty(analysis.Findings);
        Assert.Equal(0, _repository.SavedFindingCounts[1]);
    }

    [Fact]
    public async Task ExecuteAsync_DumpSemDimensoes_NaoGravaAchados()
    {
        var analysis = AddAnalysis();
        _extractor.Json = ReadMetadataJson("jpeg-format-warning.json");

        await Execute(analysis.Id);

        Assert.Equal(AnalysisStatus.Failed, analysis.Status);
        Assert.Empty(analysis.Findings);
    }

    [Fact]
    public async Task ExecuteAsync_ImagemIlegivel_GravaFailedComInvalidImageFormat()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new MetadataExtractionException(MetadataExtractionFailure.UnreadableImage, "File format error.");

        await Execute(analysis.Id);

        Assert.Equal(AnalysisFailureType.InvalidImageFormat, analysis.Failure?.Type);
    }

    [Theory]
    [InlineData(MetadataExtractionFailure.ToolUnavailable)]
    [InlineData(MetadataExtractionFailure.Timeout)]
    [InlineData(MetadataExtractionFailure.ToolFailed)]
    public async Task ExecuteAsync_FalhaDaFerramenta_GravaFailedComAnalyzerCrashed(MetadataExtractionFailure failure)
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new MetadataExtractionException(failure, "Detalhe técnico.");

        await Execute(analysis.Id);

        Assert.Equal(AnalysisFailureType.AnalyzerCrashed, analysis.Failure?.Type);
    }

    [Fact]
    public async Task ExecuteAsync_ErroInesperado_GravaFailedComAnalyzerCrashed()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new InvalidOperationException("Erro inesperado.");

        await Execute(analysis.Id);

        Assert.Equal(AnalysisFailureType.AnalyzerCrashed, analysis.Failure?.Type);
    }

    [Fact]
    public async Task ExecuteAsync_MensagemDaFalhaNaoExpoeDetalheTecnico()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new MetadataExtractionException(MetadataExtractionFailure.ToolFailed, "exiftool: exit code 1 em /usr/bin");

        await Execute(analysis.Id);

        Assert.NotNull(analysis.Failure);
        Assert.DoesNotContain("exit code", analysis.Failure.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CanceladoDuranteAExtracao_PropagaSemGravarFalha()
    {
        var analysis = AddAnalysis();
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        _extractor.Exception = new OperationCanceledException(stopping.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateUseCase().ExecuteAsync(new ProcessImageAnalysisCommand(analysis.Id), stopping.Token));

        Assert.Equal([AnalysisStatus.Processing], _repository.SavedStatuses);
        Assert.Equal(AnalysisStatus.Processing, analysis.Status);
        Assert.Null(analysis.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_CancelamentoQueNaoVemDoHost_GravaFailedComAnalyzerCrashed()
    {
        var analysis = AddAnalysis();
        _extractor.Exception = new OperationCanceledException();

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Failed, outcome);
        Assert.Equal(AnalysisFailureType.AnalyzerCrashed, analysis.Failure?.Type);
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

    [Fact]
    public async Task ExecuteAsync_AnaliseConcluida_RemoveDoCacheAsEntradasDaAnalise()
    {
        var analysis = AddAnalysis();
        var otherAnalysisId = Guid.NewGuid();
        await _cache.SetAsync(analysis.Id, "ela", "mapa");
        await _cache.SetAsync(analysis.Id, "regions", "regioes");
        await _cache.SetAsync(otherAnalysisId, "ela", "outra");

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Completed, outcome);
        Assert.Equal([analysis.Id], _cache.RemoveAllCalls.Select(c => c.AnalysisId));
        Assert.Equal([(otherAnalysisId, "ela")], _cache.Entries.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseFalha_RemoveDoCacheAsEntradasDaAnalise()
    {
        var analysis = AddAnalysis();
        await _cache.SetAsync(analysis.Id, "ela", "mapa");
        _extractor.Exception = new MetadataExtractionException(MetadataExtractionFailure.ToolFailed, "exiftool falhou");

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Failed, outcome);
        Assert.Equal([analysis.Id], _cache.RemoveAllCalls.Select(c => c.AnalysisId));
        Assert.Empty(_cache.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_LimpezaDoCacheNaoPodeSerCancelada()
    {
        var analysis = AddAnalysis();
        using var stopping = new CancellationTokenSource();

        await CreateUseCase()
            .ExecuteAsync(new ProcessImageAnalysisCommand(analysis.Id), stopping.Token);

        Assert.False(Assert.Single(_cache.RemoveAllCalls).CancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task ExecuteAsync_FalhaNaLimpezaDoCache_NaoAlteraOResultado()
    {
        var analysis = AddAnalysis();
        _cache.RemoveAllException = new InvalidOperationException("Redis indisponível.");

        var outcome = await Execute(analysis.Id);

        Assert.Equal(ProcessImageAnalysisOutcome.Completed, outcome);
        Assert.Equal(AnalysisStatus.Completed, analysis.Status);
        Assert.Null(analysis.Failure);
        Assert.Single(_cache.RemoveAllCalls);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseInexistente_NaoLimpaOCache()
    {
        await Execute(Guid.NewGuid());

        Assert.Empty(_cache.RemoveAllCalls);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseEmProcessamento_NaoLimpaOCacheDaOutraExecucao()
    {
        var analysis = AddAnalysis();
        analysis.Start();
        await _cache.SetAsync(analysis.Id, "ela", "mapa");

        await Execute(analysis.Id);

        Assert.Empty(_cache.RemoveAllCalls);
        Assert.Single(_cache.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_ConflitoAoIniciar_NaoLimpaOCache()
    {
        var analysis = AddAnalysis();
        _repository.UpdateException = new ConcurrencyConflictException(analysis.Id);

        await Execute(analysis.Id);

        Assert.Empty(_cache.RemoveAllCalls);
    }

    [Fact]
    public async Task ExecuteAsync_CanceladoPeloHost_NaoLimpaOCache()
    {
        var analysis = AddAnalysis();
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        _extractor.Exception = new OperationCanceledException(stopping.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateUseCase().ExecuteAsync(new ProcessImageAnalysisCommand(analysis.Id), stopping.Token));

        Assert.Empty(_cache.RemoveAllCalls);
    }

    [Fact]
    public async Task ExecuteAsync_ErroAoGravar_NaoLimpaOCache()
    {
        var analysis = AddAnalysis();
        _repository.UpdateException = new InvalidOperationException("Banco indisponível.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(analysis.Id));

        Assert.Empty(_cache.RemoveAllCalls);
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
        new(_repository, new MetadataAnalyzer(_storage, _extractor), _storage, _cache, NullLogger<ProcessImageAnalysis>.Instance);

    private Task<ProcessImageAnalysisOutcome> Execute(Guid analysisId) =>
        CreateUseCase().ExecuteAsync(new ProcessImageAnalysisCommand(analysisId));
}
