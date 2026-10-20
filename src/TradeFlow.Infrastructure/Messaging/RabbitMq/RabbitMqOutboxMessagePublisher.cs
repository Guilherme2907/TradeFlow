using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace TradeFlow.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Scoped publisher that opens a dedicated <see cref="IChannel"/> per batch over the Singleton <see cref="IConnection"/>,
/// enforces Publisher Confirms (<c>Basic.Ack</c>), and publishes durable messages to the <c>tradeflow.events</c> Topic Exchange (ADR-0011).
/// </summary>
public sealed class RabbitMqOutboxMessagePublisher : IOutboxMessagePublisher, IAsyncDisposable, IDisposable
{
    private readonly IRabbitMqConnectionManager _connectionManager;
    private readonly ILogger<RabbitMqOutboxMessagePublisher> _logger;
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqOutboxMessagePublisher(
        IRabbitMqConnectionManager connectionManager,
        ILogger<RabbitMqOutboxMessagePublisher> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var channel = await GetOrCreateConfirmedChannelAsync(cancellationToken);
        var routingKey = RabbitMqTopology.GetOrdersRoutingKey(eventType);
        var body = Encoding.UTF8.GetBytes(payload);

        var properties = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Type = eventType,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(occurredAt.ToUnixTimeSeconds())
        };

        // Because the channel was created with publisherConfirmationsEnabled & publisherConfirmationTrackingEnabled,
        // BasicPublishAsync awaits the broker's Publisher Confirm (Basic.Ack) before completing,
        // and throws if the broker returns Basic.Nack or disconnects.
        await channel.BasicPublishAsync(
            exchange: RabbitMqTopology.EventsExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogDebug(
            "Published and confirmed outbox message {MessageId} to exchange {Exchange} with routing key {RoutingKey}",
            messageId,
            RabbitMqTopology.EventsExchangeName,
            routingKey);
    }

    private async Task<IChannel> GetOrCreateConfirmedChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        var connection = await _connectionManager.GetConnectionAsync(cancellationToken);

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        _channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);
        await RabbitMqTopology.EnsureEventsExchangeDeclaredAsync(_channel, cancellationToken);

        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel?.Dispose();
    }
}
