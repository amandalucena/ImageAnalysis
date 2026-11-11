using RabbitMQ.Client;

namespace ImageAnalysis.Infrastructure.Messaging;

/// <summary>
/// Conexão RabbitMQ única do processo, aberta na primeira utilização. Não é aberta na
/// inicialização do host: com o RabbitMQ fora do ar, a API sobe mesmo assim, as publicações
/// falham (503) e o /health/ready mostra a indisponibilidade. Uma vez aberta, quedas são
/// tratadas pela recuperação automática do cliente, que restaura canais e consumers.
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(string connectionString)
    {
        _factory = new ConnectionFactory { Uri = new Uri(connectionString) };
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
            return _connection;

        // Evita que chamadas simultâneas abram mais de uma conexão. Se a abertura falhar,
        // nada é guardado e a próxima chamada tenta de novo.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _connection ??= await _factory.CreateConnectionAsync(cancellationToken);
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
