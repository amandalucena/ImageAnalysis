using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Service;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Exceptions;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ImageAnalysis.Application.UseCases.CreateImageAnalysis;

/// <summary>
/// Recebe a imagem enviada, registra sua impressão digital (SHA-256), grava o original no
/// storage, cria a Analysis em Pending e a envia para a fila. Não processa a imagem: isso
/// cabe ao Worker.
/// </summary>
public class CreateImageAnalysis
{
    // Tipos aceitos e a extensão usada na chave do storage. A extensão vem do tipo detectado
    // pelo conteúdo, nunca do nome enviado.
    private static readonly Dictionary<string, string> SupportedTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
    };

    // Bytes iniciais lidos para detectar o tipo (a maior assinatura reconhecida é a do PNG).
    private const int SignatureLength = 8;

    private readonly IImageStorage _imageStorage;
    // Tempo máximo para a fila confirmar a publicação.
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    private readonly IAnalysisRepository _analysisRepository;
    private readonly IAnalysisQueue _analysisQueue;
    private readonly ILogger<CreateImageAnalysis> _logger;

    public CreateImageAnalysis(
        IImageStorage imageStorage,
        IAnalysisRepository analysisRepository,
        IAnalysisQueue analysisQueue,
        ILogger<CreateImageAnalysis> logger)
    {
        _imageStorage = imageStorage;
        _analysisRepository = analysisRepository;
        _analysisQueue = analysisQueue;
        _logger = logger;
    }

    public async Task<CreateImageAnalysisResult> ExecuteAsync(CreateImageAnalysisCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OriginalFileName))
            throw new InvalidImageException("O nome do arquivo enviado é obrigatório.");

        // Os limites também são invariantes do domínio; verificados aqui para virarem erro de
        // validação (400) antes de qualquer gravação, em vez de ArgumentException (500).
        if (command.OriginalFileName.Length > ImageFile.MaxOriginalFileNameLength)
            throw new InvalidImageException(
                $"O nome do arquivo enviado deve ter no máximo {ImageFile.MaxOriginalFileNameLength} caracteres.");

        if (command.Name?.Trim().Length > Analysis.MaxNameLength)
            throw new InvalidImageException($"O nome da análise deve ter no máximo {Analysis.MaxNameLength} caracteres.");

        // O conteúdo é lido três vezes (detecção, hash e gravação), então o stream precisa permitir
        // voltar ao início. O upload do ASP.NET já chega bufferizado e é reaproveitado sem cópia;
        // só um stream sem Seek é copiado para memória.
        await using var copy = command.Content.CanSeek ? null : new MemoryStream();
        var content = command.Content;

        if (copy is not null)
        {
            await command.Content.CopyToAsync(copy, cancellationToken);
            content = copy;
        }

        if (content.Length == 0)
            throw new InvalidImageException("O arquivo enviado está vazio.");

        var signature = new byte[Math.Min(SignatureLength, content.Length)];
        content.Position = 0;
        await content.ReadExactlyAsync(signature, cancellationToken);
        var mimeType = ImageTypeDetector.GetMimeTypeFromBytes(signature);

        if (mimeType is null || !SupportedTypes.TryGetValue(mimeType, out var extension))
            throw new InvalidImageException("Tipo de arquivo não suportado. Envie uma imagem JPEG ou PNG.");

        // Devolve o stream no início, para que a gravação use exatamente os bytes do hash.
        var sha256 = await ImageHasher.ComputeSha256Async(content, cancellationToken);

        // O id do upload só compõe a chave no storage; a Analysis tem o próprio id.
        var storageKey = $"uploads/{Guid.NewGuid()}/original{extension}";

        // A Analysis é montada antes da gravação para que uma falha de validação do domínio
        // não deixe um arquivo órfão no storage.
        var image = new ImageFile(command.OriginalFileName, storageKey, content.Length, sha256, mimeType);
        var analysis = new Analysis(command.Name, image);

        // Storage antes do banco: se o banco falhar, sobra um arquivo órfão, o que é preferível
        // a uma análise apontando para uma imagem inexistente.
        await _imageStorage.SaveAsync(storageKey, content, cancellationToken);
        await _analysisRepository.AddAsync(analysis, cancellationToken);

        // Banco antes da fila: o Worker precisa encontrar a Analysis ao consumir a mensagem.
        // Com a análise já gravada, a publicação não depende do cancelamento da requisição:
        // se o cliente desistir agora, a análise ainda deve ser processada. O limite de tempo
        // evita que uma fila sem resposta prenda a requisição.
        try
        {
            using var publishTimeout = new CancellationTokenSource(PublishTimeout);
            await _analysisQueue.PublishAsync(analysis.Id, publishTimeout.Token);
        }
        catch (Exception ex)
        {
            if (await TryMarkQueueUnavailableAsync(analysis))
                throw new AnalysisQueueUnavailableException(analysis.Id, ex);

            // O Worker já tirou a análise de Pending: a mensagem chegou à fila apesar do erro
            // (ex.: confirmação perdida), então a publicação de fato aconteceu.
            _logger.LogWarning(ex, "Erro ao publicar a análise {AnalysisId}, mas ela já está sendo processada.", analysis.Id);
        }

        return new CreateImageAnalysisResult(analysis.Id);
    }

    // Sem a mensagem, nada processaria a análise e ela ficaria em Pending para sempre.
    // Retorna false se a análise já saiu de Pending (o Worker a iniciou).
    private async Task<bool> TryMarkQueueUnavailableAsync(Analysis analysis)
    {
        analysis.Fail(new AnalysisFailure(
            AnalysisFailureType.QueueUnavailable,
            "Não foi possível enviar a análise para processamento."));

        try
        {
            await _analysisRepository.UpdateAsync(analysis, CancellationToken.None);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;
        }
        catch (Exception ex)
        {
            // A falha da publicação continua sendo o erro devolvido; este fica no log. A análise
            // permanece em Pending até ser recuperada.
            _logger.LogError(ex, "Não foi possível marcar a análise {AnalysisId} como Failed.", analysis.Id);
            return true;
        }
    }
}
