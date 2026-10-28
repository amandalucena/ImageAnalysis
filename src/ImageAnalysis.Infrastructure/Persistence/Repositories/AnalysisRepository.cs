using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Exceptions;
using ImageAnalysis.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        var entry = _dbContext.Entry(analysis);
        if (entry.State == EntityState.Detached)
            throw new InvalidOperationException("A análise deve ser obtida por GetByIdAsync antes de ser atualizada.");

        // O xmin só é verificado quando a linha de Analyses recebe UPDATE. Operações que só
        // inserem filhos (AddFinding, AddEvidence, SetMetadata) não alteram a raiz, então uma
        // coluna dela é marcada como modificada para que todo save confira e incremente a versão
        // do agregado. Basta uma coluna: marcar a entidade inteira regravaria todas.
        if (entry.State == EntityState.Unchanged)
            entry.Property(a => a.Status).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity == analysis))
        {
            // Só o UPDATE da raiz confere o xmin. Uma falha em outra entidade indica erro de
            // mapeamento (ex.: filho novo tratado como existente) e não é conflito.
            throw Conflict(analysis.Id, ex);
        }
        catch (DbUpdateException ex) when (IsConcurrentChildInsert(ex))
        {
            throw Conflict(analysis.Id, ex);
        }
    }

    // Traduzida para não expor o EF Core à Application. O change tracker é limpo porque
    // continuaria com a versão desatualizada: um GetByIdAsync no mesmo contexto devolveria
    // a mesma instância rastreada, e o retry falharia de novo. Pressupõe um DbContext por
    // unidade de trabalho (um escopo por mensagem no Worker): qualquer outra entidade rastreada
    // no contexto também seria descartada.
    private ConcurrencyConflictException Conflict(Guid analysisId, DbUpdateException ex)
    {
        _dbContext.ChangeTracker.Clear();
        return new ConcurrencyConflictException(analysisId, ex);
    }

    // ImageMetadata e AnalysisResult têm índice único em AnalysisId. Se outra instância já
    // gravou o filho, o EF pode executar o INSERT antes do UPDATE da raiz, e o conflito aparece
    // como violação desse índice em vez de falha no xmin.
    private static readonly HashSet<string> UniqueChildIndexes =
    [
        "IX_ImageMetadata_AnalysisId",
        "IX_AnalysisResults_AnalysisId",
    ];

    private static bool IsConcurrentChildInsert(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName is not null
        && UniqueChildIndexes.Contains(pg.ConstraintName);
}
