using ImageAnalysis.Application.Metadata;
using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Tests.Application;

/// <summary>
/// Os arquivos de Fixtures/MetadataJson são saídas reais do ExifTool 12.57
/// (-json -G1 --System:all); os casos de borda usam JSON escrito no próprio teste.
/// </summary>
public class MetadataNormalizerTests
{
    [Fact]
    public void Normalize_FotoDeCelularCompleta_PreencheTodosOsCampos()
    {
        var metadata = Normalize(Fixture("phone-gps-thumbnail.json"));

        Assert.Equal("Apple", metadata.Make);
        Assert.Equal("iPhone 13", metadata.Model);
        Assert.Equal([new SoftwareTag("IFD0:Software", "17.4.1")], metadata.Software);

        var expectedOffset = TimeSpan.FromHours(-3);
        var expectedDate = new DateTime(2026, 3, 1, 9, 15, 44);
        Assert.Equal(new MetadataDate("2026:03:01 09:15:44", expectedDate, expectedOffset), metadata.DateTimeOriginal);
        Assert.Equal(new MetadataDate("2026:03:01 09:15:44", expectedDate, expectedOffset), metadata.CreateDate);
        Assert.Equal(new MetadataDate("2026:03:01 09:15:44", expectedDate, expectedOffset), metadata.ModifyDate);

        Assert.True(metadata.HasJfif);
        Assert.Equal("1.01", metadata.JfifVersion);
        Assert.True(metadata.HasThumbnail);
        Assert.True(metadata.HasGps);
        Assert.Equal(new ImageDimensions(160, 120), metadata.Dimensions);
        Assert.Equal(new ImageDimensions(4032, 3024), metadata.DeclaredDimensions);
        Assert.Empty(metadata.Warnings);
    }

    [Fact]
    public void Normalize_SoftwareEmExifEXmp_MantemAmbosComAOrigem()
    {
        var metadata = Normalize(Fixture("edited-photoshop.json"));

        Assert.Equal(
            [
                new SoftwareTag("IFD0:Software", "Adobe Photoshop 25.0 (Windows)"),
                new SoftwareTag("XMP-xmp:CreatorTool", "Adobe Photoshop 25.0 (Windows)")
            ],
            metadata.Software);
    }

    [Fact]
    public void Normalize_CadaDataUsaOSeuProprioOffset()
    {
        // Só OffsetTimeOriginal existe: CreateDate e ModifyDate ficam sem fuso.
        var metadata = Normalize(Fixture("edited-photoshop.json"));

        Assert.Equal(TimeSpan.FromHours(-3), metadata.DateTimeOriginal!.Offset);
        Assert.Null(metadata.CreateDate!.Offset);
        Assert.Null(metadata.ModifyDate!.Offset);
        Assert.Equal(new DateTime(2026, 3, 10, 14, 22, 1), metadata.ModifyDate.Value);
    }

    [Fact]
    public void Normalize_DataSemOffset_NaoAssumeFuso()
    {
        var metadata = Normalize(Fixture("edited-photoshop.json"));

        Assert.Equal(DateTimeKind.Unspecified, metadata.ModifyDate!.Value!.Value.Kind);
    }

    [Fact]
    public void Normalize_BlocoGpsSemCoordenadas_HasGpsFalse()
    {
        var metadata = Normalize(Fixture("gps-version-only.json"));

        Assert.False(metadata.HasGps);
        Assert.Equal("NIKON CORPORATION", metadata.Make);
    }

    [Fact]
    public void Normalize_SemThumbnailNemDimensaoDeclarada_FicamAusentes()
    {
        var metadata = Normalize(Fixture("edited-photoshop.json"));

        Assert.False(metadata.HasThumbnail);
        Assert.Null(metadata.DeclaredDimensions);
    }

    [Theory]
    [InlineData("gif.json", 40, 30)]
    [InlineData("bmp.json", 40, 30)]
    public void Normalize_OutrosFormatos_LeDimensoesDoGrupoCerto(string fixture, int width, int height)
    {
        var metadata = Normalize(Fixture(fixture));

        Assert.Equal(new ImageDimensions(width, height), metadata.Dimensions);
        Assert.False(metadata.HasJfif);
    }

    [Fact]
    public void Normalize_Png_LeDimensoesDoGrupoPng()
    {
        var metadata = Normalize("""[{ "File:FileType": "PNG", "PNG:ImageWidth": 64, "PNG:ImageHeight": 48 }]""");

        Assert.Equal(new ImageDimensions(64, 48), metadata.Dimensions);
    }

    [Fact]
    public void Normalize_AvisoDoExifTool_EntraNaListaDeAvisos()
    {
        var metadata = Normalize(Fixture("jpeg-format-warning.json"));

        Assert.Equal(["JPEG format error"], metadata.Warnings);
        Assert.Null(metadata.Dimensions);
    }

    [Fact]
    public void Normalize_SemNenhumMetadado_TudoAusenteSemFalhar()
    {
        var metadata = Normalize("""[{ "SourceFile": "-" }]""");

        Assert.Null(metadata.Make);
        Assert.Null(metadata.Model);
        Assert.Empty(metadata.Software);
        Assert.Null(metadata.DateTimeOriginal);
        Assert.Null(metadata.CreateDate);
        Assert.Null(metadata.ModifyDate);
        Assert.False(metadata.HasJfif);
        Assert.Null(metadata.JfifVersion);
        Assert.False(metadata.HasThumbnail);
        Assert.False(metadata.HasGps);
        Assert.Null(metadata.Dimensions);
        Assert.Null(metadata.DeclaredDimensions);
        Assert.Empty(metadata.Warnings);
    }

    [Theory]
    [InlineData("0000:00:00 00:00:00")]
    [InlineData("    :  :     :  :  ")]
    [InlineData("2026-03-01 09:15:44")]
    [InlineData("2026:02:30 09:15:44")]
    public void Normalize_DataInvalida_PreservaRawSemValor(string raw)
    {
        var metadata = Normalize($$"""[{ "ExifIFD:DateTimeOriginal": "{{raw}}" }]""");

        Assert.Equal(raw.Trim(), metadata.DateTimeOriginal!.Raw);
        Assert.Null(metadata.DateTimeOriginal.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_DataVazia_TratadaComoAusente(string raw)
    {
        var metadata = Normalize($$"""[{ "ExifIFD:DateTimeOriginal": "{{raw}}" }]""");

        Assert.Null(metadata.DateTimeOriginal);
    }

    [Theory]
    [InlineData("+05:30", 5, 30)]
    [InlineData("-12:00", -12, 0)]
    [InlineData("+14:00", 14, 0)]
    [InlineData("+00:00", 0, 0)]
    public void Normalize_OffsetValido_Converte(string offset, int hours, int minutes)
    {
        var metadata = Normalize(
            $$"""[{ "ExifIFD:DateTimeOriginal": "2026:03:01 09:15:44", "ExifIFD:OffsetTimeOriginal": "{{offset}}" }]""");

        var expected = new TimeSpan(Math.Abs(hours), minutes, 0);
        Assert.Equal(hours < 0 ? expected.Negate() : expected, metadata.DateTimeOriginal!.Offset);
    }

    [Theory]
    [InlineData("+15:00")]
    [InlineData("-13:00")]
    [InlineData("+03:60")]
    [InlineData("03:00")]
    [InlineData("-3:00")]
    [InlineData("abc")]
    public void Normalize_OffsetInvalido_FicaNuloEMantemAData(string offset)
    {
        var metadata = Normalize(
            $$"""[{ "ExifIFD:DateTimeOriginal": "2026:03:01 09:15:44", "ExifIFD:OffsetTimeOriginal": "{{offset}}" }]""");

        Assert.Null(metadata.DateTimeOriginal!.Offset);
        Assert.NotNull(metadata.DateTimeOriginal.Value);
    }

    [Fact]
    public void Normalize_TextoComPadding_RemoveEspacosECaracteresNulos()
    {
        var metadata = Normalize("""[{ "IFD0:Make": "  Canon\u0000\u0000", "IFD0:Model": "\u0000" }]""");

        Assert.Equal("Canon", metadata.Make);
        Assert.Null(metadata.Model);
    }

    [Fact]
    public void Normalize_ValorNumerico_LidoComoTextoSemAlterarFormato()
    {
        var metadata = Normalize("""[{ "IFD0:Model": 5, "JFIF:JFIFVersion": 1.10 }]""");

        Assert.Equal("5", metadata.Model);
        Assert.Equal("1.10", metadata.JfifVersion);
    }

    [Fact]
    public void Normalize_SemExif_UsaMakeEModelDoXmp()
    {
        var metadata = Normalize("""[{ "XMP-tiff:Make": "Sony", "XMP-tiff:Model": "ILCE-7M3" }]""");

        Assert.Equal("Sony", metadata.Make);
        Assert.Equal("ILCE-7M3", metadata.Model);
    }

    [Fact]
    public void Normalize_ExifEXmpComMakeDiferente_PrefereExif()
    {
        var metadata = Normalize("""[{ "XMP-tiff:Make": "Sony", "IFD0:Make": "Canon" }]""");

        Assert.Equal("Canon", metadata.Make);
    }

    [Fact]
    public void Normalize_SoftwareComListaDeValores_Ignora()
    {
        var metadata = Normalize("""[{ "XMP-xmp:CreatorTool": ["A", "B"], "IFD0:Software": "GIMP 2.10" }]""");

        Assert.Equal([new SoftwareTag("IFD0:Software", "GIMP 2.10")], metadata.Software);
    }

    [Fact]
    public void Normalize_TagsComposite_NaoContamComoFonte()
    {
        // Composite é deduzido pelo ExifTool a partir de outras tags, não lido do arquivo.
        var metadata = Normalize(
            """[{ "Composite:GPSPosition": "23 deg 33' 0.00\" S, 46 deg 37' 48.00\" W", "Composite:ImageSize": "64x48" }]""");

        Assert.False(metadata.HasGps);
        Assert.Null(metadata.Dimensions);
    }

    [Fact]
    public void Normalize_GpsSoNoXmp_HasGpsTrue()
    {
        var metadata = Normalize("""[{ "XMP-exif:GPSLatitude": "23 deg 33' 0.00\" S" }]""");

        Assert.True(metadata.HasGps);
    }

    [Theory]
    [InlineData("0", "48")]
    [InlineData("-64", "48")]
    [InlineData("64", "\"abc\"")]
    public void Normalize_DimensaoInvalida_FicaNulaSemFalhar(string width, string height)
    {
        var metadata = Normalize($$"""[{ "File:ImageWidth": {{width}}, "File:ImageHeight": {{height}} }]""");

        Assert.Null(metadata.Dimensions);
    }

    [Fact]
    public void Normalize_DimensaoComoTexto_Converte()
    {
        var metadata = Normalize("""[{ "File:ImageWidth": "64", "File:ImageHeight": "48" }]""");

        Assert.Equal(new ImageDimensions(64, 48), metadata.Dimensions);
    }

    [Fact]
    public void Normalize_VariosAvisosEmLista_MantemTodos()
    {
        var metadata = Normalize("""[{ "ExifTool:Warning": ["Aviso 1", " ", "Aviso 2"] }]""");

        Assert.Equal(["Aviso 1", "Aviso 2"], metadata.Warnings);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{ "IFD0:Make": "Canon" }""")]
    [InlineData("[]")]
    [InlineData("""[{ "a": 1 }, { "b": 2 }]""")]
    public void Normalize_DumpForaDoFormato_LancaArgumentException(string json)
    {
        Assert.Throws<ArgumentException>(() => Normalize(json));
    }

    [Fact]
    public void Normalize_Nulo_LancaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MetadataNormalizer.Normalize(null!));
    }

    private static NormalizedMetadata Normalize(string json) => MetadataNormalizer.Normalize(new RawMetadata(json));

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MetadataJson", name));
}
