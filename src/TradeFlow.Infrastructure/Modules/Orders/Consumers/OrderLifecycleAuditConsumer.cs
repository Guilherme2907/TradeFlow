using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeFlow.Infrastructure.Messaging;

namespace TradeFlow.Infrastructure.Modules.Orders.Consumers;

/// <summary>
/// Asynchronous consumer bound to the wildcard routing key <c>orders.*</c> on the Topic Exchange,
/// capturing every order lifecycle event (<c>OrderCreatedEvent</c>, <c>OrderLineAddedEvent</c>,
/// <c>OrderSubmittedEvent</c>, <c>OrderApprovedEvent</c>, <c>OrderCancelledEvent</c>) for compliance auditing (ADR-0011).
/// </summary>
public sealed class OrderLifecycleAuditConsumer : IIntegrationEventConsumer
{
    private readonly ILogger<OrderLifecycleAuditConsumer> _logger;

    public OrderLifecycleAuditConsumer(ILogger<OrderLifecycleAuditConsumer> logger)
    {
        _logger = logger;
    }

    public string ConsumerName => nameof(OrderLifecycleAuditConsumer);
    public string QueueName => "audit.order-events";
    public string RoutingKeyPattern => "orders.*";

    public Task HandleAsync(
        Guid messageId,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var orderId = root.GetProperty("orderId").GetProperty("value").GetGuid();

        _logger.LogInformation(
            "[{Consumer}] Recorded compliance audit trail for Order {OrderId}: {EventType} (EventId: {MessageId})",
            ConsumerName,
            orderId,
            eventType,
            messageId);

        return Task.CompletedTask;
    }
}
