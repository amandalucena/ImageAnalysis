using ImageAnalysis.Domain.ValueObjects;

namespace ImageAnalysis.Tests.Domain;

public class ImageFileTests
{
    [Fact]
    public void Construtor_NomeDoArquivoNoLimite_Aceita()
    {
        var fileName = new string('a', ImageFile.MaxOriginalFileNameLength - 4) + ".jpg";

        var image = NewImage(fileName);

        Assert.Equal(fileName, image.OriginalFileName);
    }

    [Fact]
    public void Construtor_NomeDoArquivoAcimaDoLimite_LancaExcecao()
    {
        var fileName = new string('a', ImageFile.MaxOriginalFileNameLength) + ".jpg";

        Assert.Throws<ArgumentException>(() => NewImage(fileName));
    }

    private static ImageFile NewImage(string originalFileName) => new(
        originalFileName,
        storagePath: $"uploads/{Guid.NewGuid()}/original.jpg",
        sizeInBytes: 1_024,
        sha256: new string('a', 64),
        mimeType: "image/jpeg");
}
