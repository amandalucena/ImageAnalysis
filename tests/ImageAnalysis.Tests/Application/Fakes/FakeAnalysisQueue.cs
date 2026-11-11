using ImageAnalysis.Domain.Interfaces;

namespace ImageAnalysis.Tests.Application.Fakes;

public class FakeAnalysisQueue : IAnalysisQueue
{
    public List<Guid> Published { get; } = new();

    // Quando definida, PublishAsync lança esta exceção em vez de publicar.
    public Exception? PublishException { get; set; }

    public Task PublishAsync(Guid analysisId, CancellationToken cancellationToken = default)
    {
        if (PublishException is not null)
            throw PublishException;

        Published.Add(analysisId);
        return Task.CompletedTask;
    }
}
