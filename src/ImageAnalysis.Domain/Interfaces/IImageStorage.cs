namespace ImageAnalysis.Domain.Interfaces;

/// <summary>
/// Armazenamento dos arquivos de imagem fora do banco relacional.
/// As chaves são relativas (ex.: uploads/{uploadId}/original.jpg) para não depender
/// da implementação: disco local hoje, object storage em nuvem futuramente.
/// </summary>
public interface IImageStorage
{
    // Grava um arquivo novo. Lança exceção se a chave já existir: a imagem original
    // nunca é sobrescrita.
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
