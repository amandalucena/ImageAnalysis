using ImageAnalysis.Application.Storage;

namespace ImageAnalysis.Tests.Application;

public class AnalysisStorageKeysTests
{
    [Fact]
    public void RawMetadata_IdValido_DevolveChaveNaPastaDaAnalise()
    {
        var analysisId = Guid.Parse("3f2b8c1e-9a4d-4e6f-b1c2-7d8e9f0a1b2c");

        var key = AnalysisStorageKeys.RawMetadata(analysisId);

        Assert.Equal("analysis-3f2b8c1e-9a4d-4e6f-b1c2-7d8e9f0a1b2c/metadata.json", key);
    }

    [Fact]
    public void RawMetadata_IdVazio_LancaExcecao()
    {
        Assert.Throws<ArgumentException>(() => AnalysisStorageKeys.RawMetadata(Guid.Empty));
    }
}
