using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Tests.Application.Fakes;

public class FakeAnalysisRepository : IAnalysisRepository
{
    public List<Analysis> Analyses { get; } = new();

    public Task<Analysis?> GetByIdAsync(Guid analysisId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Analyses.SingleOrDefault(a => a.Id == analysisId));

    public Task AddAsync(Analysis analysis, CancellationToken cancellationToken = default)
    {
        Analyses.Add(analysis);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Analysis analysis, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
