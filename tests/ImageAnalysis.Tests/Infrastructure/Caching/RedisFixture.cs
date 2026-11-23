using ImageAnalysis.Infrastructure.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace ImageAnalysis.Tests.Infrastructure.Caching;

/// <summary>
/// Sobe um Redis descartável (Testcontainers), compartilhado entre os testes da coleção "Redis".
/// Cada teste usa um analysisId próprio, então os testes não interferem entre si. Requer Docker.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7")
        .Build();

    public RedisConnectionProvider ConnectionProvider { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionProvider = new RedisConnectionProvider(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await ConnectionProvider.DisposeAsync();
        await _container.DisposeAsync();
    }

    // Acesso direto ao Redis, para verificar chaves e TTLs sem passar pelo cache.
    public Task<IDatabase> GetDatabaseAsync() => ConnectionProvider.GetDatabaseAsync();
}

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
}
