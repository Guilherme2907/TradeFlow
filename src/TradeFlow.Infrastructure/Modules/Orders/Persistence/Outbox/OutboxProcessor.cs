using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradeFlow.Infrastructure.Messaging;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;

/// <summary>
/// Polls unprocessed outbox messages from <c>orders.outbox_messages</c> using
/// <c>SELECT ... FOR UPDATE SKIP LOCKED</c> to guarantee non-blocking concurrency across multiple worker replicas,
/// publishes them via <see cref="IOutboxMessagePublisher"/>, and updates their processing state (ADR-0011).
/// </summary>
public sealed class OutboxProcessor
{
    public const int DefaultBatchSize = 20;
    public const int MaxRetryCount = 5;

    private readonly OrdersDbContext _dbContext;
    private readonly IOutboxMessagePublisher _publisher;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        OrdersDbContext dbContext,
        IOutboxMessagePublisher publisher,
        ILogger<OutboxProcessor> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Claims a batch of unprocessed outbox messages under a row-level lock (<c>SKIP LOCKED</c>),
    /// dispatches each message to the broker, and commits the updated state.
    /// </summary>
    /// <returns>The number of candidate messages fetched and processed in this batch.</returns>
    public async Task<int> ProcessBatchAsync(
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
    {
        // An explicit transaction is mandatory so PostgreSQL holds the FOR UPDATE row locks
        // while this worker is actively publishing the batch to the broker.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var messages = await _dbContext.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT id, event_type, payload, occurred_at, processed_at, error, retry_count
                FROM orders.outbox_messages
                WHERE processed_at IS NULL AND retry_count < {MaxRetryCount}
                ORDER BY occurred_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        foreach (var message in messages)
        {
            try
            {
                await _publisher.PublishAsync(
                    message.Id,
                    message.EventType,
                    message.Payload,
                    message.OccurredAt,
                    cancellationToken);

                message.MarkAsProcessed(DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to publish outbox message {MessageId} ({EventType}). Attempt {Attempt}/{MaxRetryCount}",
                    message.Id,
                    message.EventType,
                    message.RetryCount + 1,
                    MaxRetryCount);

                message.RecordFailure(ex.Message);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return messages.Count;
    }
}
