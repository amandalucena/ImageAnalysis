using ImageAnalysis.Application.Exceptions;

namespace ImageAnalysis.Application.Metadata;

/// <summary>
/// Extrai os metadados de uma imagem sem interpretá-los: devolve o dump bruto da ferramenta,
/// que é guardado inteiro para auditoria e normalizado depois (CLAUDE.md, seção 9).
/// </summary>
public interface IMetadataExtractor
{
    /// <summary>
    /// Lê o conteúdo de <paramref name="image"/> até o fim. O stream não é fechado.
    /// </summary>
    /// <exception cref="MetadataExtractionException">
    /// A ferramenta não está disponível, excedeu o tempo limite, não conseguiu ler o arquivo
    /// ou devolveu uma saída inesperada.
    /// </exception>
    Task<RawMetadata> ExtractAsync(Stream image, CancellationToken cancellationToken = default);
}
