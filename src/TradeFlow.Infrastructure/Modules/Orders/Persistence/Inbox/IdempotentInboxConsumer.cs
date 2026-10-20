using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Inbox;

/// <summary>
/// Implements the Idempotent Consumer (Inbox Pattern) using PostgreSQL <c>INSERT ... ON CONFLICT DO NOTHING</c>
/// within the same local transaction as the consumer's business logic, turning At-Least-Once broker delivery
/// into Effectively-Once processing (ADR-0011).
/// </summary>
public sealed class IdempotentInboxConsumer
{
    private readonly OrdersDbContext _dbContext;
    private readonly ILogger<IdempotentInboxConsumer> _logger;

    public IdempotentInboxConsumer(
        OrdersDbContext dbContext,
        ILogger<IdempotentInboxConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Atomically claims <c>(messageId, consumerName)</c> in <c>orders.inbox_messages</c> and executes <paramref name="handler"/>
    /// inside a single PostgreSQL transaction.
    /// </summary>
    /// <returns><c>true</c> if the message was processed for the first time; <c>false</c> if skipped as a duplicate.</returns>
    public async Task<bool> ExecuteIdempotentlyAsync(
        Guid messageId,
        string consumerName,
        string eventType,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(handler);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var processedAt = DateTimeOffset.UtcNow;

        // Atomic deduplication guard:
        // 1. If (message_id, consumer_name) already exists, returns 0 rows affected without throwing.
        // 2. If two threads race on the exact same message concurrently, PostgreSQL's unique index lock
        //    serializes the second INSERT until the first transaction commits or rolls back.
        // 3. If the business handler throws later in this method, the transaction rolls back the inbox row too.
        var rowsInserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO orders.inbox_messages (message_id, consumer_name, event_type, processed_at)
            VALUES ({messageId}, {consumerName}, {eventType}, {processedAt})
            ON CONFLICT (message_id, consumer_name) DO NOTHING;
            """, cancellationToken);

        if (rowsInserted == 0)
        {
            _logger.LogInformation(
                "Duplicate message {MessageId} for consumer {ConsumerName} detected in inbox. Skipping execution.",
                messageId,
                consumerName);

            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await handler(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Consumes the next available message from a RabbitMQ queue with manual acknowledgment (<c>autoAck: false</c>),
    /// executes the handler idempotently via the PostgreSQL Inbox table, and sends <c>Basic.Ack</c> (or <c>Basic.Nack</c> to DLQ on failure).
    /// </summary>
    /// <returns>
    /// <c>true</c> if a message was consumed and processed for the first time;
    /// <c>false</c> if a duplicate message was consumed, deduplicated, and ACKed;
    /// <c>null</c> if the queue was empty.
    /// </returns>
    public async Task<bool?> ConsumeNextFromQueueAsync(
        IChannel channel,
        string queueName,
        string consumerName,
        Func<Guid, string, string, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        var delivery = await channel.BasicGetAsync(queueName, autoAck: false, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId))
        {
            _logger.LogError(
                "Message on queue {QueueName} has invalid or missing MessageId '{MessageId}'. Routing to DLQ.",
                queueName,
                delivery.BasicProperties.MessageId);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return false;
        }

        var eventType = delivery.BasicProperties.Type ?? "Unknown";
        var payload = Encoding.UTF8.GetString(delivery.Body.ToArray());

        try
        {
            var wasExecuted = await ExecuteIdempotentlyAsync(
                messageId,
                consumerName,
                eventType,
                ct => handler(messageId, eventType, payload, ct),
                cancellationToken);

            // Send Basic.Ack ONLY after the database transaction (inbox + side-effects) has committed
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            return wasExecuted;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Consumer {ConsumerName} failed processing message {MessageId} from queue {QueueName}. Sending Basic.Nack (DLQ).",
                consumerName,
                messageId,
                queueName);

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            throw;
        }
    }
}
