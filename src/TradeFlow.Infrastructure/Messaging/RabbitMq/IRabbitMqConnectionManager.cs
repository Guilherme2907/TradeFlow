using RabbitMQ.Client;

namespace TradeFlow.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Manages the Singleton physical TCP <see cref="IConnection"/> to RabbitMQ,
/// preventing ephemeral port exhaustion while allowing scoped components to open multiplexed <see cref="IChannel"/> instances.
/// </summary>
public interface IRabbitMqConnectionManager : IAsyncDisposable, IDisposable
{
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
}
