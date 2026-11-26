using ImageAnalysis.Application.Jpeg;

namespace ImageAnalysis.Tests.Application.Jpeg;

/// <summary>
/// Leitura completa dos JPEGs de Fixtures/Metadata, gerados por encoders reais, como teste de
/// sanidade do leitor e do decoder fora dos arquivos montados byte a byte.
/// </summary>
public class JpegStructureParserFixtureTests
{
    [Theory]
    [InlineData("camera.jpg")]
    [InlineData("edited.jpg")]
    [InlineData("no-metadata.jpg")]
    public void Parse_JpegReal_LeAEstruturaSemProblemas(string fileName)
    {
        var structure = JpegStructureParser.Parse(Fixture(fileName));

        Assert.Empty(structure.Issues);
        Assert.True(structure.HasEndOfImage);
        Assert.Equal(JpegMarker.Soi, structure.Segments[0].Marker);
        Assert.Equal(JpegMarker.Eoi, structure.Segments[^1].Marker);

        var frame = Assert.Single(structure.Frames);
        Assert.True(frame.Width > 0);
        Assert.True(frame.Height > 0);

        Assert.NotEmpty(structure.QuantizationTables);
        Assert.NotEmpty(structure.HuffmanTables);
        Assert.NotEmpty(structure.Scans);

        // Toda tabela referenciada pelo frame existe no arquivo.
        Assert.All(frame.Components, c => Assert.Contains(structure.QuantizationTables, t => t.Id == c.QuantizationTableId));
    }

    [Fact]
    public void Parse_FotoDeCamera_IdentificaOExif()
    {
        var structure = JpegStructureParser.Parse(Fixture("camera.jpg"));

        Assert.Contains(structure.ApplicationSegments, a => a.Kind == JpegApplicationKind.Exif);
    }

    [Fact]
    public void Parse_ArquivoPng_NaoTemSoi()
    {
        var structure = JpegStructureParser.Parse(Fixture("no-metadata.png"));

        Assert.Equal(JpegReadIssueKind.MissingStartOfImage, Assert.Single(structure.Issues).Kind);
    }

    private static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Metadata", fileName));
}
