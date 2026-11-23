using System.Text;
using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Application.Pipeline;
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
/// grava o dump bruto em analysis-{id}/metadata.json e registra o ImageMetadata e os achados
/// com suas evidências. A consolidação
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
            var metadata = await _metadataAnalyzer.AnalyzeMetadataAsync(
                new AnalyzerContext(analysis.Id, analysis.Image), cancellationToken);

            // Sem dimensões o ExifTool leu o arquivo, mas não a estrutura da imagem (ex.: JPEG
            // malformado). A checagem vem antes do storage para não deixar um metadata.json órfão.
            // Isso descarta o achado de aviso de leitura desse arquivo; mantê-lo exigiria
            // dimensões opcionais no ImageMetadata.
            if (metadata.Normalized.Dimensions is not { } dimensions)
            {
                _logger.LogWarning(
                    "A análise {AnalysisId} falhou: o ExifTool não devolveu as dimensões da imagem.", analysis.Id);

                analysis.Fail(new AnalysisFailure(
                    AnalysisFailureType.InvalidImageFormat,
                    "Não foi possível ler a estrutura da imagem."));

                return ProcessImageAnalysisOutcome.Failed;
            }

            await SaveMetadataAsync(analysis, metadata.Raw, dimensions, cancellationToken);

            // Último passo antes do Complete: o Domain não remove achados, então uma falha depois
            // deste ponto gravaria uma análise Failed com achados. Nada que possa falhar deve
            // entrar entre esta linha e o Complete.
            AnalyzerResultApplier.Apply(analysis, _metadataAnalyzer.Source, metadata.Result);

            analysis.Complete(
                AnalysisResultStatus.Inconclusive,
                ConfidenceLevel.Low,
                "A análise de metadados foi executada, mas os achados ainda não são consolidados num resultado.",
                "Esta versão do sistema ainda não consolida as evidências; o resultado não avalia a imagem.");

            return ProcessImageAnalysisOutcome.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Encerramento do host: não é falha da análise. Ela fica em Processing, sem gravação
            // final, e o Worker não confirma a mensagem. Marcar Failed impediria a recuperação
            // dessas análises, que é escopo da #65.
            throw;
        }
        catch (Exception ex)
        {
            var failure = ToFailure(ex);

            // O detalhe técnico fica no log; a análise guarda só uma mensagem segura para exibição.
            _logger.LogError(ex, "Erro ao processar a análise {AnalysisId} ({FailureType}).", analysis.Id, failure.Type);

            analysis.Fail(failure);

            return ProcessImageAnalysisOutcome.Failed;
        }
    }

    // A imagem ilegível é problema do arquivo enviado; storage e ferramenta são problemas do
    // sistema. Uma IOException que chega aqui vem quase sempre do storage (leitura da imagem ou
    // gravação do dump): o extrator trata os erros de escrita no stdin do ExifTool. Limitação
    // conhecida: um erro de leitura no stdout/stderr do ExifTool também é IOException e cai em
    // StorageUnavailable. Convertê-lo no extrator rotularia errado a leitura da imagem, que
    // acontece no mesmo trecho.
    private static AnalysisFailure ToFailure(Exception exception) => exception switch
    {
        MetadataExtractionException { Failure: MetadataExtractionFailure.UnreadableImage } => new(
            AnalysisFailureType.InvalidImageFormat,
            "Não foi possível ler a imagem enviada."),

        MetadataExtractionException => new(
            AnalysisFailureType.AnalyzerCrashed,
            "Não foi possível extrair os metadados da imagem."),

        // Antes da IOException, da qual deriva: a imagem sumiu de vez, e uma nova tentativa
        // não resolveria. A mensagem não deve sugerir indisponibilidade temporária.
        FileNotFoundException => new(
            AnalysisFailureType.StorageUnavailable,
            "A imagem original não foi encontrada no armazenamento."),

        IOException or UnauthorizedAccessException => new(
            AnalysisFailureType.StorageUnavailable,
            "Não foi possível acessar o armazenamento de arquivos."),

        _ => new(
            AnalysisFailureType.AnalyzerCrashed,
            "Ocorreu um erro durante o processamento da análise."),
    };

    // O storage é gravado antes do agregado: o RawMetadataPath nunca aponta para um arquivo
    // que não existe. O ImageMetadata vai para o banco no UpdateAsync final, junto com o resto.
    private async Task SaveMetadataAsync(
        Analysis analysis, RawMetadata raw, ImageDimensions dimensions, CancellationToken cancellationToken)
    {
        var key = AnalysisStorageKeys.RawMetadata(analysis.Id);

        // GetBytes não escreve BOM: o arquivo é o texto do ExifTool em UTF-8, sem acréscimos.
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes(raw.Json), writable: false))
        {
            await _storage.SaveAsync(key, content, cancellationToken);
        }

        analysis.SetMetadata(key, dimensions);
    }
}
