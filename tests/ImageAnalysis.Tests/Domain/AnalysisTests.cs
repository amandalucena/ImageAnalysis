using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Enums;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Tests.Domain;

public class AnalysisTests
{
    [Fact]
    public void Construtor_ComNome_MantemNome()
    {
        var analysis = new Analysis("Análise de teste", NewImage());

        Assert.Equal("Análise de teste", analysis.Name);
        Assert.Equal(AnalysisStatus.Pending, analysis.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Construtor_SemNome_NomeFicaNulo(string? name)
    {
        var analysis = new Analysis(name, NewImage());

        Assert.Null(analysis.Name);
        Assert.Equal(AnalysisStatus.Pending, analysis.Status);
    }

    [Fact]
    public void Construtor_NomeComEspacosNasPontas_RemoveEspacos()
    {
        var analysis = new Analysis("  Análise de teste  ", NewImage());

        Assert.Equal("Análise de teste", analysis.Name);
    }

    [Fact]
    public void Construtor_SemImagem_LancaExcecao()
    {
        Assert.Throws<ArgumentNullException>(() => new Analysis("Análise de teste", null!));
    }

    private static ImageFile NewImage() => new(
        originalFileName: "foto.jpg",
        storagePath: $"uploads/{Guid.NewGuid()}/original.jpg",
        sizeInBytes: 1_024,
        sha256: new string('a', 64),
        mimeType: "image/jpeg");
}
