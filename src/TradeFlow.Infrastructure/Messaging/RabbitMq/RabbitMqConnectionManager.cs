using RabbitMQ.Client;

namespace TradeFlow.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Thread-safe Singleton manager that lazily opens and reuses a single physical TCP <see cref="IConnection"/>
/// across the application lifetime (ADR-0011).
/// </summary>
public sealed class RabbitMqConnectionManager : IRabbitMqConnectionManager
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IConnection? _connection;
    private bool _disposed;

    public RabbitMqConnectionManager(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
            return _connection;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _semaphore.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection?.Dispose();
        _semaphore.Dispose();
    }
}
