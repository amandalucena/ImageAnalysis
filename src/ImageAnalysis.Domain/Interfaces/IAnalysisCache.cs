namespace ImageAnalysis.Domain.Interfaces;

/// <summary>
/// Cache temporário dos dados intermediários de uma análise em execução (ex.: características
/// do ELA, regiões candidatas, estatísticas), trocados entre analisadores (CLAUDE.md 8.1 e 14).
/// Não é a fonte definitiva de nada: o resultado final vai para o banco, e arquivos grandes
/// (mapas, máscaras, heatmaps) vão para o IImageStorage.
///
/// Cada entrada pertence a uma análise e é identificada por um nome (ex.: "ela", "regions").
/// A implementação monta a chave (ex.: analysis:{id}:ela) e serializa o valor, para que quem
/// chama não dependa do formato nem da tecnologia do cache.
/// </summary>
public interface IAnalysisCache
{
    // Grava ou substitui a entrada. Sem timeToLive, a implementação aplica um TTL de segurança,
    // para que entradas de análises interrompidas não fiquem no cache para sempre.
    Task SetAsync<T>(Guid analysisId, string entry, T value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);

    // Retorna default (null para tipos de referência) se a entrada não existir ou já tiver expirado.
    Task<T?> GetAsync<T>(Guid analysisId, string entry, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid analysisId, string entry, CancellationToken cancellationToken = default);

    // Remove todas as entradas da análise. Chamado ao fim do processamento, com sucesso ou falha.
    Task RemoveAllAsync(Guid analysisId, CancellationToken cancellationToken = default);
}
