using ImageAnalysis.Application.Metadata;

namespace ImageAnalysis.Tests.Application;

public class KnownSoftwareTests
{
    [Theory]
    [InlineData("Adobe Photoshop 25.0 (Windows)")]
    [InlineData("Adobe Photoshop Lightroom Classic 13.0 (Macintosh)")]
    [InlineData("GIMP 2.10.36")]
    [InlineData("Affinity Photo 2.4.0")]
    [InlineData("Pixelmator Pro 3.5")]
    [InlineData("Snapseed 2.0")]
    [InlineData("Canva")]
    [InlineData("adobe photoshop cc 2019")]
    public void IsEditor_ValorComEditorConhecido_RetornaTrue(string software)
    {
        Assert.True(KnownSoftware.IsEditor(software));
    }

    [Theory]
    [InlineData("Midjourney")]
    [InlineData("DALL-E 3")]
    [InlineData("DALL·E")]
    [InlineData("Stable Diffusion XL")]
    [InlineData("Adobe Firefly")]
    [InlineData("ComfyUI")]
    [InlineData("stable diffusion")]
    public void IsAiGenerator_ValorComGeradorConhecido_RetornaTrue(string software)
    {
        Assert.True(KnownSoftware.IsAiGenerator(software));
    }

    [Theory]
    [InlineData("Canvas")]          // não é Canva
    [InlineData("Photo")]           // pedaço de Photoshop
    [InlineData("HDR+ 1.0.540104767zd")]
    [InlineData("Ver.1.00")]        // firmware de câmera
    [InlineData("iOS 17.4")]
    public void IsEditor_ValorSemEditorConhecido_RetornaFalse(string software)
    {
        Assert.False(KnownSoftware.IsEditor(software));
    }

    [Theory]
    [InlineData("Adobe Photoshop 25.0 (Windows)")]
    [InlineData("GIMP 2.10.36")]
    [InlineData("Ver.1.00")]
    public void IsAiGenerator_ValorSemGeradorConhecido_RetornaFalse(string software)
    {
        Assert.False(KnownSoftware.IsAiGenerator(software));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValorVazio_NaoCorrespondeANada(string? software)
    {
        Assert.False(KnownSoftware.IsEditor(software));
        Assert.False(KnownSoftware.IsAiGenerator(software));
    }

    [Fact]
    public void ValorComEditorEGerador_CorrespondeAosDois()
    {
        const string software = "Adobe Photoshop 25.0 (Firefly)";

        Assert.True(KnownSoftware.IsEditor(software));
        Assert.True(KnownSoftware.IsAiGenerator(software));
    }
}
