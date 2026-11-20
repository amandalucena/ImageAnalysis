using ImageAnalysis.Application.Exceptions;
using ImageAnalysis.Infrastructure.ExternalTools.ExifTool;

namespace ImageAnalysis.Tests.Infrastructure.ExternalTools;

public class ExifToolOutputTests
{
    private const string ValidOutput = """
        [{
          "SourceFile": "-",
          "File:FileType": "JPEG",
          "IFD0:Make": "Canon"
        }]
        """;

    [Fact]
    public void Interpret_SaidaValida_DevolveJsonSemAlteracao()
    {
        var metadata = ExifToolOutput.Interpret(0, ValidOutput, "");

        Assert.Equal(ValidOutput, metadata.Json);
    }

    [Fact]
    public void Interpret_ComAviso_NaoEFalha()
    {
        const string output = """[{ "ExifTool:Warning": "JPEG format error", "File:FileType": "JPEG" }]""";

        var metadata = ExifToolOutput.Interpret(0, output, "");

        Assert.Equal(output, metadata.Json);
    }

    [Fact]
    public void Interpret_ComErroDeLeitura_LancaUnreadableImage()
    {
        const string output = """[{ "SourceFile": "-", "ExifTool:Error": "Unknown file type" }]""";

        var ex = Assert.Throws<MetadataExtractionException>(() => ExifToolOutput.Interpret(1, output, ""));

        Assert.Equal(MetadataExtractionFailure.UnreadableImage, ex.Failure);
        Assert.Contains("Unknown file type", ex.Message);
    }

    [Fact]
    public void Interpret_SemSaida_LancaToolFailedComStderr()
    {
        var ex = Assert.Throws<MetadataExtractionException>(
            () => ExifToolOutput.Interpret(2, "", "Can't locate Image/ExifTool.pm"));

        Assert.Equal(MetadataExtractionFailure.ToolFailed, ex.Failure);
        Assert.Contains("Can't locate Image/ExifTool.pm", ex.Message);
    }

    [Fact]
    public void Interpret_StderrLongo_TruncaNaMensagem()
    {
        var ex = Assert.Throws<MetadataExtractionException>(
            () => ExifToolOutput.Interpret(2, "", new string('x', 5000)));

        Assert.True(ex.Message.Length < 1000);
    }

    [Fact]
    public void Interpret_ExitCodeDiferenteDeZeroSemErroDeLeitura_LancaToolFailed()
    {
        var ex = Assert.Throws<MetadataExtractionException>(() => ExifToolOutput.Interpret(1, ValidOutput, ""));

        Assert.Equal(MetadataExtractionFailure.ToolFailed, ex.Failure);
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("""{ "File:FileType": "JPEG" }""")]
    [InlineData("""[{ "SourceFile": "a" }, { "SourceFile": "b" }]""")]
    [InlineData("[]")]
    [InlineData("""["texto"]""")]
    public void Interpret_SaidaEmFormatoInesperado_LancaToolFailed(string output)
    {
        var ex = Assert.Throws<MetadataExtractionException>(() => ExifToolOutput.Interpret(0, output, ""));

        Assert.Equal(MetadataExtractionFailure.ToolFailed, ex.Failure);
    }
}
