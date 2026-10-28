using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Tests.Application.Fakes;

// Guarda em memória o conteúdo recebido, para conferir exatamente os bytes gravados.
public class FakeImageStorage : IImageStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        if (Files.ContainsKey(key))
            throw new IOException($"O arquivo {key} já existe.");

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        Files[key] = copy.ToArray();
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.ContainsKey(key));
}
