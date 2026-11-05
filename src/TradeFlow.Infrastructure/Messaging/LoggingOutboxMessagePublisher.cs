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
        string? traceParent = null,
        string? traceState = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Published outbox message {MessageId} of type {EventType} (OccurredAt: {OccurredAt}, TraceParent: {TraceParent})",
            messageId,
            eventType,
            occurredAt,
            traceParent);

        return Task.CompletedTask;
    }
}
