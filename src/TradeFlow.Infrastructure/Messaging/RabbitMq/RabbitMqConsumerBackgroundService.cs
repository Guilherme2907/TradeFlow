using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradeFlow.Infrastructure.Modules.Orders.Persistence.Inbox;

namespace TradeFlow.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Push-based <see cref="BackgroundService"/> that opens one dedicated <see cref="IChannel"/> per registered
/// <see cref="IIntegrationEventConsumer"/> over the Singleton <see cref="IConnection"/>, configures
/// Dead-Letter Queues and <c>BasicQos</c> backpressure, and delegates incoming messages to
/// <see cref="IdempotentInboxConsumer"/> before sending <c>Basic.Ack</c> (ADR-0011).
/// </summary>
public sealed class RabbitMqConsumerBackgroundService : BackgroundService
{
    private const ushort DefaultPrefetchCount = 10;

    private readonly IRabbitMqConnectionManager _connectionManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RabbitMqConsumerBackgroundService> _logger;

    public RabbitMqConsumerBackgroundService(
        IRabbitMqConnectionManager connectionManager,
        IServiceScopeFactory scopeFactory,
        ILogger<RabbitMqConsumerBackgroundService> logger)
    {
        _connectionManager = connectionManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<(string ConsumerName, string QueueName, string RoutingKeyPattern)> consumerDefinitions;
        await using (var discoveryScope = _scopeFactory.CreateAsyncScope())
        {
            consumerDefinitions = discoveryScope.ServiceProvider
                .GetServices<IIntegrationEventConsumer>()
                .Select(c => (c.ConsumerName, c.QueueName, c.RoutingKeyPattern))
                .ToList();
        }

        if (consumerDefinitions.Count == 0)
        {
            return;
        }

        var openChannels = new List<IChannel>(consumerDefinitions.Count);

        try
        {
            var connection = await _connectionManager.GetConnectionAsync(stoppingToken);

            foreach (var definition in consumerDefinitions)
            {
                // Dedicated IChannel per consumer (never shared across concurrent consumers)
                var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                openChannels.Add(channel);

                await RabbitMqTopology.DeclareQueueWithDeadLetteringAsync(
                    channel,
                    definition.QueueName,
                    definition.RoutingKeyPattern,
                    stoppingToken);

                // Backpressure: limit unacknowledged messages in flight per consumer channel
                await channel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: DefaultPrefetchCount,
                    global: false,
                    cancellationToken: stoppingToken);

                var amqpConsumer = new AsyncEventingBasicConsumer(channel);
                var capturedDefinition = definition;

                amqpConsumer.ReceivedAsync += async (_, delivery) =>
                {
                    await HandleDeliveryAsync(channel, capturedDefinition.ConsumerName, capturedDefinition.QueueName, delivery, stoppingToken);
                };

                await channel.BasicConsumeAsync(
                    queue: definition.QueueName,
                    autoAck: false,
                    consumer: amqpConsumer,
                    cancellationToken: stoppingToken);

                _logger.LogInformation(
                    "Started RabbitMQ push consumer {ConsumerName} on queue '{QueueName}' (RoutingKey: '{RoutingKey}')",
                    definition.ConsumerName,
                    definition.QueueName,
                    definition.RoutingKeyPattern);
            }

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal graceful shutdown
        }
        finally
        {
            foreach (var channel in openChannels)
            {
                try
                {
                    await channel.DisposeAsync();
                }
                catch
                {
                    // Ignore channel disposal errors during host shutdown
                }
            }
        }
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        string consumerName,
        string queueName,
        BasicDeliverEventArgs delivery,
        CancellationToken stoppingToken)
    {
        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
        {
            _logger.LogError(
                "Message on queue '{QueueName}' has missing/invalid MessageId '{MessageId}'. Routing to DLQ.",
                queueName,
                delivery.BasicProperties.MessageId);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        var eventType = delivery.BasicProperties.Type ?? "Unknown";
        var payload = Encoding.UTF8.GetString(delivery.Body.ToArray());

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var inboxConsumer = scope.ServiceProvider.GetRequiredService<IdempotentInboxConsumer>();
            var targetConsumer = scope.ServiceProvider
                .GetServices<IIntegrationEventConsumer>()
                .Single(c => c.ConsumerName == consumerName);

            await inboxConsumer.ExecuteIdempotentlyAsync(
                messageId,
                consumerName,
                eventType,
                ct => targetConsumer.HandleAsync(messageId, eventType, payload, ct),
                stoppingToken);

            // Acknowledge to RabbitMQ ONLY after PostgreSQL transaction (inbox + handler) has committed
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Consumer {ConsumerName} failed processing message {MessageId} on queue '{QueueName}'. Sending Basic.Nack to DLQ.",
                consumerName,
                messageId,
                queueName);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, stoppingToken);
        }
    }
}
