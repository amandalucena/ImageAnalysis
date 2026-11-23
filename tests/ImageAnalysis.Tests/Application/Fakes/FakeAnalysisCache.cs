using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Tests.Application.Fakes;

// Guarda as entradas em memória e registra as limpezas, para conferir quando o cache é limpo.
public class FakeAnalysisCache : IAnalysisCache
{
    public Dictionary<(Guid AnalysisId, string Entry), object?> Entries { get; } = new();

    // Análise e token de cada RemoveAllAsync, na ordem das chamadas.
    public List<(Guid AnalysisId, CancellationToken CancellationToken)> RemoveAllCalls { get; } = new();

    // Quando definida, RemoveAllAsync lança esta exceção em vez de remover.
    public Exception? RemoveAllException { get; set; }

    public Task SetAsync<T>(Guid analysisId, string entry, T value, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default)
    {
        Entries[(analysisId, entry)] = value;
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(Guid analysisId, string entry, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.TryGetValue((analysisId, entry), out var value) ? (T?)value : default);

    public Task RemoveAsync(Guid analysisId, string entry, CancellationToken cancellationToken = default)
    {
        Entries.Remove((analysisId, entry));
        return Task.CompletedTask;
    }

    public Task RemoveAllAsync(Guid analysisId, CancellationToken cancellationToken = default)
    {
        RemoveAllCalls.Add((analysisId, cancellationToken));

        if (RemoveAllException is not null)
            throw RemoveAllException;

        foreach (var key in Entries.Keys.Where(k => k.AnalysisId == analysisId).ToList())
            Entries.Remove(key);

        return Task.CompletedTask;
    }
}
