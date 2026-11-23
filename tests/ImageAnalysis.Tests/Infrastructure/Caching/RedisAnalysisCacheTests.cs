using ImageAnalysis.Infrastructure.Caching;

namespace ImageAnalysis.Tests.Infrastructure.Caching;

[Collection("Redis")]
public class RedisAnalysisCacheTests
{
    private readonly RedisFixture _fixture;
    private readonly RedisAnalysisCache _cache;

    public RedisAnalysisCacheTests(RedisFixture fixture)
    {
        _fixture = fixture;
        _cache = new RedisAnalysisCache(fixture.ConnectionProvider);
    }

    private sealed record ElaStats(double Mean, double P95, int[] Histogram);

    [Fact]
    public async Task SetAsync_GetAsync_DevolveOValorGravado()
    {
        var analysisId = Guid.NewGuid();
        var stats = new ElaStats(12.5, 40.25, [1, 2, 3]);

        await _cache.SetAsync(analysisId, "ela", stats);
        var loaded = await _cache.GetAsync<ElaStats>(analysisId, "ela");

        Assert.NotNull(loaded);
        Assert.Equal(stats.Mean, loaded.Mean);
        Assert.Equal(stats.P95, loaded.P95);
        Assert.Equal(stats.Histogram, loaded.Histogram);
    }

    [Fact]
    public async Task SetAsync_GravaNaChaveDaAnalise()
    {
        var analysisId = Guid.NewGuid();

        await _cache.SetAsync(analysisId, "regions", new[] { 1, 2 });

        var database = await _fixture.GetDatabaseAsync();
        Assert.True(await database.KeyExistsAsync($"analysis:{analysisId}:regions"));
    }

    [Fact]
    public async Task SetAsync_EntradaExistente_SubstituiOValor()
    {
        var analysisId = Guid.NewGuid();

        await _cache.SetAsync(analysisId, "ela", "primeiro");
        await _cache.SetAsync(analysisId, "ela", "segundo");

        Assert.Equal("segundo", await _cache.GetAsync<string>(analysisId, "ela"));
    }

    [Fact]
    public async Task GetAsync_EntradaInexistente_DevolveDefault()
    {
        var analysisId = Guid.NewGuid();

        Assert.Null(await _cache.GetAsync<ElaStats>(analysisId, "ela"));
        Assert.Equal(0, await _cache.GetAsync<int>(analysisId, "count"));
    }

    [Fact]
    public async Task SetAsync_SemTtl_AplicaTtlPadrao()
    {
        var analysisId = Guid.NewGuid();

        await _cache.SetAsync(analysisId, "ela", "valor");

        var database = await _fixture.GetDatabaseAsync();
        var ttl = await database.KeyTimeToLiveAsync($"analysis:{analysisId}:ela");
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, RedisAnalysisCache.DefaultTimeToLive - TimeSpan.FromMinutes(1), RedisAnalysisCache.DefaultTimeToLive);
    }

    [Fact]
    public async Task SetAsync_ComTtl_AplicaOTtlInformado()
    {
        var analysisId = Guid.NewGuid();
        var timeToLive = TimeSpan.FromMinutes(10);

        await _cache.SetAsync(analysisId, "ela", "valor", timeToLive);

        var database = await _fixture.GetDatabaseAsync();
        var ttl = await database.KeyTimeToLiveAsync($"analysis:{analysisId}:ela");
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, timeToLive - TimeSpan.FromMinutes(1), timeToLive);
    }

    [Fact]
    public async Task SetAsync_EntradaExpira()
    {
        var analysisId = Guid.NewGuid();

        await _cache.SetAsync(analysisId, "ela", "valor", TimeSpan.FromMilliseconds(100));
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Null(await _cache.GetAsync<string>(analysisId, "ela"));
    }

    [Fact]
    public async Task SetAsync_IndiceNuncaExpiraAntesDaEntradaMaisLonga()
    {
        var analysisId = Guid.NewGuid();

        await _cache.SetAsync(analysisId, "ela", "longa", TimeSpan.FromHours(2));
        await _cache.SetAsync(analysisId, "regions", "curta", TimeSpan.FromMinutes(5));

        var database = await _fixture.GetDatabaseAsync();
        var indexTtl = await database.KeyTimeToLiveAsync($"analysis:{analysisId}:_entries");
        Assert.NotNull(indexTtl);
        Assert.True(indexTtl.Value > TimeSpan.FromHours(1), $"TTL do índice: {indexTtl}");
    }

    [Fact]
    public async Task RemoveAsync_RemoveSoAEntradaInformada()
    {
        var analysisId = Guid.NewGuid();
        await _cache.SetAsync(analysisId, "ela", "a");
        await _cache.SetAsync(analysisId, "regions", "b");

        await _cache.RemoveAsync(analysisId, "ela");

        Assert.Null(await _cache.GetAsync<string>(analysisId, "ela"));
        Assert.Equal("b", await _cache.GetAsync<string>(analysisId, "regions"));

        var database = await _fixture.GetDatabaseAsync();
        Assert.False(await database.SetContainsAsync($"analysis:{analysisId}:_entries", "ela"));
    }

    [Fact]
    public async Task RemoveAsync_EntradaInexistente_NaoFalha()
    {
        await _cache.RemoveAsync(Guid.NewGuid(), "ela");
    }

    [Fact]
    public async Task RemoveAllAsync_RemoveEntradasEIndiceSemAfetarOutraAnalise()
    {
        var analysisId = Guid.NewGuid();
        var otherAnalysisId = Guid.NewGuid();
        await _cache.SetAsync(analysisId, "ela", "a");
        await _cache.SetAsync(analysisId, "regions", "b");
        await _cache.SetAsync(analysisId, "features", "c");
        await _cache.SetAsync(otherAnalysisId, "ela", "outra");

        await _cache.RemoveAllAsync(analysisId);

        var database = await _fixture.GetDatabaseAsync();
        Assert.False(await database.KeyExistsAsync($"analysis:{analysisId}:ela"));
        Assert.False(await database.KeyExistsAsync($"analysis:{analysisId}:regions"));
        Assert.False(await database.KeyExistsAsync($"analysis:{analysisId}:features"));
        Assert.False(await database.KeyExistsAsync($"analysis:{analysisId}:_entries"));
        Assert.Equal("outra", await _cache.GetAsync<string>(otherAnalysisId, "ela"));
    }

    [Fact]
    public async Task RemoveAllAsync_AnaliseSemEntradas_NaoFalha()
    {
        await _cache.RemoveAllAsync(Guid.NewGuid());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ELA")]
    [InlineData("ela:stats")]
    [InlineData("_entries")]
    [InlineData("-ela")]
    [InlineData("ela stats")]
    public async Task NomeDeEntradaInvalido_LancaArgumentException(string entry)
    {
        var analysisId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => _cache.SetAsync(analysisId, entry, "valor"));
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.GetAsync<string>(analysisId, entry));
        await Assert.ThrowsAsync<ArgumentException>(() => _cache.RemoveAsync(analysisId, entry));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetAsync_TtlNaoPositivo_LancaArgumentOutOfRangeException(int seconds)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _cache.SetAsync(Guid.NewGuid(), "ela", "valor", TimeSpan.FromSeconds(seconds)));
    }
}
