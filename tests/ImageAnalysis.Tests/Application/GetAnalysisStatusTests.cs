using ImageAnalysis.Application.UseCases.GetAnalysisStatus;
using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.ValueObjects;
using ImageAnalysis.Tests.Application.Fakes;

namespace ImageAnalysis.Tests.Application;

public class GetAnalysisStatusTests
{
    private readonly FakeAnalysisRepository _repository = new();

    [Fact]
    public async Task ExecuteAsync_AnaliseExistente_RetornaStatusEDadosBasicos()
    {
        var image = new ImageFile("foto.jpg", $"uploads/{Guid.NewGuid()}/original.jpg", 1_024, new string('a', 64), "image/jpeg");
        var analysis = new Analysis("Análise de teste", image);
        _repository.Analyses.Add(analysis);

        var result = await new GetAnalysisStatus(_repository).ExecuteAsync(new GetAnalysisStatusQuery(analysis.Id));

        Assert.NotNull(result);
        Assert.Equal(analysis.Id, result.Id);
        Assert.Equal("Análise de teste", result.Name);
        Assert.Equal("Pending", result.Status);
        Assert.Equal("foto.jpg", result.OriginalFileName);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(1_024, result.SizeInBytes);
        Assert.Equal(new string('a', 64), result.Sha256);
        Assert.Equal(analysis.CreatedAt, result.CreatedAt);
        Assert.Null(result.StartedAt);
        Assert.Null(result.FinishedAt);
    }

    [Fact]
    public async Task ExecuteAsync_AnaliseInexistente_RetornaNull()
    {
        var result = await new GetAnalysisStatus(_repository).ExecuteAsync(new GetAnalysisStatusQuery(Guid.NewGuid()));

        Assert.Null(result);
    }
}
