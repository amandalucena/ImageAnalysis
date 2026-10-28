using System.Text;
using ImageAnalysis.Application.Service;

namespace ImageAnalysis.Tests.Application;

public class ImageHasherTests
{
    // Vetores de teste publicados para o SHA-256.
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public async Task ComputeSha256Async_VetoresConhecidos_RetornaHashEsperado(string input, string expected)
    {
        using var content = new MemoryStream(Encoding.ASCII.GetBytes(input));

        var hash = await ImageHasher.ComputeSha256Async(content);

        Assert.Equal(expected, hash);
    }

    [Fact]
    public async Task ComputeSha256Async_StreamForaDoInicio_CalculaSobreArquivoInteiroEVoltaAoInicio()
    {
        using var content = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        content.Position = 2;

        var hash = await ImageHasher.ComputeSha256Async(content);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        Assert.Equal(0, content.Position);
    }

    [Fact]
    public async Task ComputeSha256Async_StreamSemSeek_LancaExcecao()
    {
        using var content = new NonSeekableStream(Encoding.ASCII.GetBytes("abc"));

        await Assert.ThrowsAsync<ArgumentException>(() => ImageHasher.ComputeSha256Async(content));
    }

    private sealed class NonSeekableStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
