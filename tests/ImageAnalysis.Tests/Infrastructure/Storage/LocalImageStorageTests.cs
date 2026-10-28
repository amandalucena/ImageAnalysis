using ImageAnalysis.Infrastructure.Storage;

namespace ImageAnalysis.Tests.Infrastructure.Storage;

public class LocalImageStorageTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"image-analysis-tests-{Guid.NewGuid()}");
    private readonly LocalImageStorage _storage;

    public LocalImageStorageTests()
    {
        _storage = new LocalImageStorage(_rootPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_ChaveNova_GravaConteudoNaPastaRaiz()
    {
        byte[] content = [1, 2, 3, 4];
        var key = $"uploads/{Guid.NewGuid()}/original.jpg";

        await _storage.SaveAsync(key, new MemoryStream(content));

        Assert.True(await _storage.ExistsAsync(key));
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(_rootPath, key)));
    }

    [Fact]
    public async Task SaveAsync_ChaveExistente_LancaExcecaoEMantemOriginal()
    {
        byte[] original = [1, 2, 3, 4];
        var key = $"uploads/{Guid.NewGuid()}/original.jpg";
        await _storage.SaveAsync(key, new MemoryStream(original));

        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(key, new MemoryStream([9, 9, 9])));

        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(_rootPath, key)));
    }

    [Theory]
    [InlineData("../fora.jpg")]
    [InlineData("uploads/../../fora.jpg")]
    [InlineData("")]
    public async Task SaveAsync_ChaveForaDoStorage_LancaExcecao(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(key, new MemoryStream([1])));
    }

    [Fact]
    public async Task SaveAsync_CaminhoAbsoluto_LancaExcecao()
    {
        var absoluteKey = Path.Combine(Path.GetTempPath(), $"fora-{Guid.NewGuid()}.jpg");

        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(absoluteKey, new MemoryStream([1])));

        Assert.False(File.Exists(absoluteKey));
    }

    [Fact]
    public async Task SaveAsync_FalhaDuranteACopia_NaoDeixaArquivoIncompleto()
    {
        var key = $"uploads/{Guid.NewGuid()}/original.jpg";

        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(key, new FailingStream()));

        Assert.False(await _storage.ExistsAsync(key));
    }

    [Fact]
    public async Task SaveAsync_FalhaDuranteACopia_RemovePastaCriadaParaAGravacao()
    {
        var uploadId = Guid.NewGuid();

        await Assert.ThrowsAsync<IOException>(
            () => _storage.SaveAsync($"uploads/{uploadId}/original.jpg", new FailingStream()));

        Assert.False(Directory.Exists(Path.Combine(_rootPath, "uploads", uploadId.ToString())));
    }

    [Fact]
    public async Task SaveAsync_ChaveExistente_MantemPastaComOriginal()
    {
        var key = $"uploads/{Guid.NewGuid()}/original.jpg";
        await _storage.SaveAsync(key, new MemoryStream([1, 2, 3]));

        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(key, new MemoryStream([9])));

        Assert.True(await _storage.ExistsAsync(key));
    }

    [Theory]
    [InlineData("..cache/original.png")]
    [InlineData("uploads/..arquivo.jpg")]
    public async Task SaveAsync_NomeComecandoComDoisPontosDentroDaRaiz_Aceita(string key)
    {
        await _storage.SaveAsync(key, new MemoryStream([1, 2, 3]));

        Assert.True(await _storage.ExistsAsync(key));
    }

    [Fact]
    public async Task ExistsAsync_ChaveInexistente_RetornaFalse()
    {
        Assert.False(await _storage.ExistsAsync($"uploads/{Guid.NewGuid()}/original.jpg"));
    }

    private sealed class FailingStream : MemoryStream
    {
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            throw new IOException("Falha simulada na leitura do upload.");
    }
}
