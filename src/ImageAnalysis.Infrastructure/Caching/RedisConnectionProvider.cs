using StackExchange.Redis;

namespace ImageAnalysis.Infrastructure.Caching;

/// <summary>
/// Conexão Redis única do processo, aberta na primeira utilização. Segue o mesmo princípio do
/// RabbitMqConnectionProvider: não é aberta na inicialização do host, então a API sobe com o
/// Redis fora do ar e o /health/ready mostra a indisponibilidade. Uma vez aberta, quedas são
/// tratadas pela reconexão automática do ConnectionMultiplexer.
/// </summary>
public sealed class RedisConnectionProvider : IAsyncDisposable
{
    private readonly ConfigurationOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnectionMultiplexer? _connection;

    public RedisConnectionProvider(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A connection string do Redis é obrigatória.", nameof(connectionString));

        _options = ConfigurationOptions.Parse(connectionString);

        // Falha na primeira conexão lança exceção em vez de devolver um multiplexer desconectado:
        // nada é guardado e a próxima chamada tenta de novo, como no RabbitMqConnectionProvider.
        _options.AbortOnConnectFail = true;
    }

    public async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
            return _connection.GetDatabase();

        // Evita que chamadas simultâneas abram mais de uma conexão.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _connection ??= await ConnectionMultiplexer.ConnectAsync(_options);
            return _connection.GetDatabase();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _lock.Dispose();
    }
}
