using ImageAnalysis.Domain.Entities;

namespace ImageAnalysis.Domain.Interfaces;

/// <summary>
/// Repositório do agregado Analysis. Findings, Evidences, ImageMetadata e AnalysisResult
/// são persistidos através da Analysis, sem repositórios próprios.
/// Não há remoção: análises são mantidas para histórico e auditoria.
/// </summary>
public interface IAnalysisRepository
{
    // Retorna a Analysis completa (ImageMetadata, Result, Findings e Evidences).
    Task<Analysis?> GetByIdAsync(Guid analysisId, CancellationToken cancellationToken = default);

    Task AddAsync(Analysis analysis, CancellationToken cancellationToken = default);

    // Grava as alterações de uma Analysis obtida por GetByIdAsync.
    // Lança ConcurrencyConflictException se a análise foi gravada por outra operação depois
    // da leitura; nesse caso nada é gravado e a análise precisa ser obtida de novo.
    Task UpdateAsync(Analysis analysis, CancellationToken cancellationToken = default);
}
