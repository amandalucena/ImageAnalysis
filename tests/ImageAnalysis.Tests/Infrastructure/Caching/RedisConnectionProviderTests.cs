using ImageAnalysis.Infrastructure.Caching;
using StackExchange.Redis;

namespace ImageAnalysis.Tests.Infrastructure.Caching;

/// <summary>
/// Comportamento com o Redis inacessível (porta 1 em localhost, conexão recusada). Não usa Docker.
/// </summary>
public class RedisConnectionProviderTests
{
    private const string UnreachableRedis = "localhost:1,connectTimeout=1000";

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConnectionStringVazia_LancaArgumentException(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => new RedisConnectionProvider(connectionString));
    }

    [Fact]
    public async Task Construtor_NaoAbreConexao()
    {
        // Não lança: a conexão só é tentada no GetDatabaseAsync.
        await using var provider = new RedisConnectionProvider(UnreachableRedis);
    }

    [Fact]
    public async Task GetDatabaseAsync_RedisInacessivel_LancaETentaDeNovoNaProximaChamada()
    {
        await using var provider = new RedisConnectionProvider(UnreachableRedis);

        await Assert.ThrowsAsync<RedisConnectionException>(() => provider.GetDatabaseAsync());

        // A falha não fica guardada: a segunda chamada tenta conectar de novo e falha outra vez.
        await Assert.ThrowsAsync<RedisConnectionException>(() => provider.GetDatabaseAsync());
    }

    [Fact]
    public async Task Cache_RedisInacessivel_PropagaAFalha()
    {
        await using var provider = new RedisConnectionProvider(UnreachableRedis);
        var cache = new RedisAnalysisCache(provider);

        await Assert.ThrowsAsync<RedisConnectionException>(() => cache.SetAsync(Guid.NewGuid(), "ela", "valor"));
    }
}
