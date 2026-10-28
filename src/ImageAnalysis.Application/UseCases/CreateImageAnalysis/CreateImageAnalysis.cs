using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Application.Service;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Interfaces;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Application.UseCases.CreateImageAnalysis;

/// <summary>
/// Recebe a imagem enviada, registra sua impressão digital (SHA-256), grava o original no
/// storage e cria a Analysis em Pending. Não processa a imagem: isso cabe ao Worker.
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
    private readonly IAnalysisRepository _analysisRepository;

    public CreateImageAnalysis(IImageStorage imageStorage, IAnalysisRepository analysisRepository)
    {
        _imageStorage = imageStorage;
        _analysisRepository = analysisRepository;
    }

    public async Task<CreateImageAnalysisResult> ExecuteAsync(CreateImageAnalysisCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OriginalFileName))
            throw new InvalidImageException("O nome do arquivo enviado é obrigatório.");

        // O stream do upload normalmente não permite voltar ao início, e o conteúdo é lido três
        // vezes (detecção, hash e gravação). Com o limite de upload de 20 MB, cabe em memória.
        using var buffer = new MemoryStream();
        await command.Content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length == 0)
            throw new InvalidImageException("O arquivo enviado está vazio.");

        var signature = buffer.GetBuffer().AsSpan(0, (int)Math.Min(SignatureLength, buffer.Length)).ToArray();
        var mimeType = ImageTypeDetector.GetMimeTypeFromBytes(signature);

        if (mimeType is null || !SupportedTypes.TryGetValue(mimeType, out var extension))
            throw new InvalidImageException("Tipo de arquivo não suportado. Envie uma imagem JPEG ou PNG.");

        var sha256 = await ImageHasher.ComputeSha256Async(buffer, cancellationToken);

        // O id do upload só compõe a chave no storage; a Analysis tem o próprio id.
        var storageKey = $"uploads/{Guid.NewGuid()}/original{extension}";

        // A Analysis é montada antes da gravação para que uma falha de validação do domínio
        // não deixe um arquivo órfão no storage.
        var image = new ImageFile(command.OriginalFileName, storageKey, buffer.Length, sha256, mimeType);
        var analysis = new Analysis(command.Name, image);

        // Storage antes do banco: se o banco falhar, sobra um arquivo órfão, o que é preferível
        // a uma análise apontando para uma imagem inexistente.
        await _imageStorage.SaveAsync(storageKey, buffer, cancellationToken);
        await _analysisRepository.AddAsync(analysis, cancellationToken);

        return new CreateImageAnalysisResult(analysis.Id);
    }
}
