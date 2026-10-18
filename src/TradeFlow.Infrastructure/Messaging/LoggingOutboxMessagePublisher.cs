using Microsoft.Extensions.Logging;

namespace TradeFlow.Infrastructure.Messaging;

/// <summary>
/// Default structured-logging publisher used when an external broker is not explicitly configured.
/// </summary>
public sealed class LoggingOutboxMessagePublisher : IOutboxMessagePublisher
{
    private readonly ILogger<LoggingOutboxMessagePublisher> _logger;

    public LoggingOutboxMessagePublisher(ILogger<LoggingOutboxMessagePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Published outbox message {MessageId} of type {EventType} (OccurredAt: {OccurredAt})",
            messageId,
            eventType,
            occurredAt);

        return Task.CompletedTask;
    }
}
