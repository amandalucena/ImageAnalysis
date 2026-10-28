using ImageAnalysis.Application.Service;

namespace ImageAnalysis.Tests.Application;

public class ImageTypeDetectorTests
{
    [Fact]
    public void GetMimeTypeFromBytes_Jpeg_RetornaImageJpeg()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9];

        Assert.Equal("image/jpeg", ImageTypeDetector.GetMimeTypeFromBytes(bytes));
    }

    [Fact]
    public void GetMimeTypeFromBytes_JpegComBytesAposEoi_RetornaImageJpeg()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x10, 0xFF, 0xD9, 0x00, 0x00];

        Assert.Equal("image/jpeg", ImageTypeDetector.GetMimeTypeFromBytes(bytes));
    }

    [Fact]
    public void GetMimeTypeFromBytes_SoiSemMarcadorSeguinte_RetornaNull()
    {
        byte[] bytes = [0xFF, 0xD8, 0x00, 0x00];

        Assert.Null(ImageTypeDetector.GetMimeTypeFromBytes(bytes));
    }

    [Fact]
    public void GetMimeTypeFromBytes_Png_RetornaImagePng()
    {
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        Assert.Equal("image/png", ImageTypeDetector.GetMimeTypeFromBytes(bytes));
    }

    [Fact]
    public void GetMimeTypeFromBytes_Pdf_RetornaNull()
    {
        byte[] bytes = "%PDF-1.7"u8.ToArray();

        Assert.Null(ImageTypeDetector.GetMimeTypeFromBytes(bytes));
    }

    [Fact]
    public void GetMimeTypeFromBytes_ArquivoMuitoPequeno_RetornaNull()
    {
        byte[] bytes = [0xFF, 0xD8];

        Assert.Null(ImageTypeDetector.GetMimeTypeFromBytes(bytes));
    }
}
