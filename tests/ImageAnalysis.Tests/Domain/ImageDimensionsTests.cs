using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Tests.Domain;

public class ImageDimensionsTests
{
    [Fact]
    public void Construtor_DimensoesValidas_MantemValores()
    {
        var dimensions = new ImageDimensions(4032, 3024);

        Assert.Equal(4032, dimensions.Width);
        Assert.Equal(3024, dimensions.Height);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void Construtor_DimensaoNaoPositiva_LancaExcecao(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDimensions(width, height));
    }

    [Fact]
    public void Igualdade_MesmasDimensoes_SaoIguais()
    {
        Assert.Equal(new ImageDimensions(800, 600), new ImageDimensions(800, 600));
        Assert.NotEqual(new ImageDimensions(800, 600), new ImageDimensions(600, 800));
    }
}
