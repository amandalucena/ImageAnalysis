using ImageAnalysis.Application.Jpeg;

namespace ImageAnalysis.Tests.Application.Jpeg;

public class JpegMarkersTests
{
    private static readonly byte[] StartOfFrameCodes =
        [0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF];

    [Fact]
    public void IsKnown_TodosOsCodigosDaT81_SaoConhecidos()
    {
        // 0x01, 0xC0–0xFE: a faixa inteira dos marcadores definidos.
        var expected = new[] { (byte)0x01 }.Concat(Enumerable.Range(0xC0, 0xFE - 0xC0 + 1).Select(c => (byte)c));

        var known = Enumerable.Range(0, 256).Select(c => (byte)c).Where(JpegMarkers.IsKnown);

        Assert.Equal(expected, known);
    }

    [Theory]
    [InlineData(0x00)] // stuffing nos dados comprimidos
    [InlineData(0x02)]
    [InlineData(0xBF)]
    [InlineData(0xFF)] // preenchimento entre marcadores
    public void IsKnown_CodigoForaDaT81_EhDesconhecido(byte code)
    {
        Assert.False(JpegMarkers.IsKnown(code));
    }

    [Fact]
    public void IsStartOfFrame_SoOsSofn()
    {
        var sof = Enumerable.Range(0, 256).Select(c => (byte)c).Where(JpegMarkers.IsStartOfFrame);

        Assert.Equal(StartOfFrameCodes, sof);
    }

    [Theory]
    [InlineData(0xC4)] // DHT
    [InlineData(0xC8)] // JPG
    [InlineData(0xCC)] // DAC
    public void IsStartOfFrame_CodigosNoMeioDaFaixaQueNaoSaoSof_RetornaFalse(byte code)
    {
        Assert.False(JpegMarkers.IsStartOfFrame(code));
    }

    [Fact]
    public void IsStandalone_SoiEoiRstETem()
    {
        var standalone = Enumerable.Range(0, 256).Select(c => (byte)c).Where(JpegMarkers.IsStandalone);

        Assert.Equal([0x01, 0xD0, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9], standalone);
    }

    [Fact]
    public void IsRestart_Rst0ARst7()
    {
        var restart = Enumerable.Range(0, 256).Select(c => (byte)c).Where(JpegMarkers.IsRestart);

        Assert.Equal(Enumerable.Range(0xD0, 8).Select(c => (byte)c), restart);
    }

    [Fact]
    public void IsApplication_App0AApp15()
    {
        var application = Enumerable.Range(0, 256).Select(c => (byte)c).Where(JpegMarkers.IsApplication);

        Assert.Equal(Enumerable.Range(0xE0, 16).Select(c => (byte)c), application);
    }

    [Fact]
    public void IsReserved_JpgEJpg0AJpg13()
    {
        var reserved = Enumerable.Range(0, 256).Select(c => (byte)c).Where(JpegMarkers.IsReserved);

        Assert.Equal(new[] { (byte)0xC8 }.Concat(Enumerable.Range(0xF0, 14).Select(c => (byte)c)), reserved);
    }

    [Theory]
    [InlineData(JpegMarker.Soi, 0xD8)]
    [InlineData(JpegMarker.Eoi, 0xD9)]
    [InlineData(JpegMarker.Sos, 0xDA)]
    [InlineData(JpegMarker.Dqt, 0xDB)]
    [InlineData(JpegMarker.Dht, 0xC4)]
    [InlineData(JpegMarker.Dri, 0xDD)]
    [InlineData(JpegMarker.App1, 0xE1)]
    [InlineData(JpegMarker.App14, 0xEE)]
    [InlineData(JpegMarker.Com, 0xFE)]
    public void JpegMarker_ValorEhOSegundoByteDoCodigo(JpegMarker marker, byte code)
    {
        Assert.Equal(code, (byte)marker);
    }
}
