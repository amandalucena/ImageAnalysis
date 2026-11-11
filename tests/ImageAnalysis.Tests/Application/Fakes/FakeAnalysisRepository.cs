using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Tests.Application.Fakes;

public class FakeAnalysisRepository : IAnalysisRepository
{
    public List<Analysis> Analyses { get; } = new();

    // Status de cada análise no momento de cada UpdateAsync, na ordem das chamadas.
    public List<AnalysisStatus> SavedStatuses { get; } = new();

    // Quando definida, UpdateAsync lança esta exceção em vez de gravar.
    public Exception? UpdateException { get; set; }

    public Task<Analysis?> GetByIdAsync(Guid analysisId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Analyses.SingleOrDefault(a => a.Id == analysisId));

    public Task AddAsync(Analysis analysis, CancellationToken cancellationToken = default)
    {
        Analyses.Add(analysis);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Analysis analysis, CancellationToken cancellationToken = default)
    {
        if (UpdateException is not null)
            throw UpdateException;

        SavedStatuses.Add(analysis.Status);
        return Task.CompletedTask;
    }
}
