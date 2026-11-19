using System.Text;
using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Application.Storage;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Exceptions;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ImageAnalysis.Application.UseCases.ProcessImageAnalysis;

/// <summary>
/// Processa uma análise recebida da fila, conduzindo o ciclo de vida
/// Pending → Processing → Completed, ou Failed em caso de erro. Executa o MetadataAnalyzer,
/// grava o dump bruto em analysis-{id}/metadata.json e registra o ImageMetadata. A consolidação
/// dos achados num resultado ainda não existe (#43–#45), então a análise conclui com um
/// resultado Inconclusive.
/// </summary>
public class ProcessImageAnalysis
{
    private readonly IAnalysisRepository _analysisRepository;
    private readonly MetadataAnalyzer _metadataAnalyzer;
    private readonly IImageStorage _storage;
    private readonly ILogger<ProcessImageAnalysis> _logger;

    // MetadataAnalyzer concreto, e não IImageAnalyzer: só o AnalyzeMetadataAsync devolve
    // o dump bruto e os metadados normalizados junto com os achados.
    public ProcessImageAnalysis(
        IAnalysisRepository analysisRepository,
        MetadataAnalyzer metadataAnalyzer,
        IImageStorage storage,
        ILogger<ProcessImageAnalysis> logger)
    {
        _analysisRepository = analysisRepository;
        _metadataAnalyzer = metadataAnalyzer;
        _storage = storage;
        _logger = logger;
    }

    public async Task<ProcessImageAnalysisOutcome> ExecuteAsync(ProcessImageAnalysisCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var analysis = await _analysisRepository.GetByIdAsync(command.AnalysisId, cancellationToken);
        if (analysis is null)
            return ProcessImageAnalysisOutcome.NotFound;

        // Idempotência: a fila pode entregar a mesma mensagem mais de uma vez.
        if (analysis.Status != AnalysisStatus.Pending)
            return ProcessImageAnalysisOutcome.Skipped;

        analysis.Start();

        try
        {
            await _analysisRepository.UpdateAsync(analysis, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Em Pending só Start e Fail alteram a análise; o conflito indica que outra
            // operação já a tirou de Pending, que é o que um GetByIdAsync novo mostraria.
            return ProcessImageAnalysisOutcome.Skipped;
        }

        // Só o processamento fica no try: uma falha ao gravar não tem como ser registrada
        // na própria análise e segue para quem consumiu a mensagem.
        var outcome = await RunAsync(analysis, cancellationToken);

        // CancellationToken.None: com a análise já em Processing, interromper esta gravação
        // (ex.: encerramento do host) a deixaria presa nesse status, porque a reentrega da
        // mensagem seria ignorada.
        await _analysisRepository.UpdateAsync(analysis, CancellationToken.None);

        return outcome;
    }

    private async Task<ProcessImageAnalysisOutcome> RunAsync(Analysis analysis, CancellationToken cancellationToken)
    {
        try
        {
            // Os achados ainda não são aplicados no agregado (7.5 item 2).
            // TODO(7.5 item 3): um cancelamento aqui (encerramento do host) cai no catch
            // abaixo e marca a análise como AnalyzerCrashed.
            var metadata = await _metadataAnalyzer.AnalyzeMetadataAsync(
                new AnalyzerContext(analysis.Id, analysis.Image), cancellationToken);

            await SaveMetadataAsync(analysis, metadata, cancellationToken);

            analysis.Complete(
                AnalysisResultStatus.Inconclusive,
                ConfidenceLevel.Low,
                "A análise de metadados foi executada, mas os achados ainda não são consolidados num resultado.",
                "Esta versão do sistema ainda não consolida as evidências; o resultado não avalia a imagem.");

            return ProcessImageAnalysisOutcome.Completed;
        }
        catch (Exception ex)
        {
            // O detalhe técnico fica no log; a análise guarda só uma mensagem segura para exibição.
            _logger.LogError(ex, "Erro ao processar a análise {AnalysisId}.", analysis.Id);

            analysis.Fail(new AnalysisFailure(
                AnalysisFailureType.AnalyzerCrashed,
                "Ocorreu um erro durante o processamento da análise."));

            return ProcessImageAnalysisOutcome.Failed;
        }
    }

    // O storage é gravado antes do agregado: o RawMetadataPath nunca aponta para um arquivo
    // que não existe. O ImageMetadata vai para o banco no UpdateAsync final, junto com o resto.
    private async Task SaveMetadataAsync(Analysis analysis, MetadataAnalysis metadata, CancellationToken cancellationToken)
    {
        var key = AnalysisStorageKeys.RawMetadata(analysis.Id);

        // GetBytes não escreve BOM: o arquivo é o texto do ExifTool em UTF-8, sem acréscimos.
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes(metadata.Raw.Json), writable: false))
        {
            await _storage.SaveAsync(key, content, cancellationToken);
        }

        // TODO(7.5 T4): sem dimensões o SetMetadata lança exceção e o metadata.json gravado
        // acima fica sem referência; a checagem precisa vir antes do SaveAsync.
        analysis.SetMetadata(key, metadata.Normalized.Dimensions!);
    }
}
