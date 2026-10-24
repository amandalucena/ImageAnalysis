using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ImageAnalysis.Infrastructure.Persistence.Repositories;

public class AnalysisRepository : IAnalysisRepository
{
    private readonly ImageAnalysisDbContext _dbContext;

    public AnalysisRepository(ImageAnalysisDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Analysis?> GetByIdAsync(Guid analysisId, CancellationToken cancellationToken = default)
    {
        // Image e Failure são owned e já vêm junto com a Analysis.
        return await _dbContext.Analyses
            .Include(a => a.Metadata)
            .Include(a => a.Result)
            .Include(a => a.Findings)
                .ThenInclude(f => f.Evidences)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == analysisId, cancellationToken);
    }

    public async Task AddAsync(Analysis analysis, CancellationToken cancellationToken = default)
    {
        await _dbContext.Analyses.AddAsync(analysis, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Analysis analysis, CancellationToken cancellationToken = default)
    {
        // A Analysis vem rastreada do GetByIdAsync; o change tracker detecta as alterações
        // e insere os filhos novos. Não usar DbSet.Update: ele marca o grafo inteiro como
        // Modified e, como os Ids são gerados no domínio, geraria UPDATE para filhos novos.
        if (_dbContext.Entry(analysis).State == EntityState.Detached)
            throw new InvalidOperationException("A análise deve ser obtida por GetByIdAsync antes de ser atualizada.");

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
