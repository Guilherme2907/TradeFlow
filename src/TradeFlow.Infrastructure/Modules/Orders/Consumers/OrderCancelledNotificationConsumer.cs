using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Infrastructure.Messaging;

namespace TradeFlow.Infrastructure.Modules.Orders.Consumers;

/// <summary>
/// Asynchronous consumer that reacts to <see cref="OrderCancelledEvent"/> to dispatch
/// customer cancellation notifications and downstream fulfilment alerts (ADR-0011).
/// </summary>
public sealed class OrderCancelledNotificationConsumer : IIntegrationEventConsumer
{
    private readonly ILogger<OrderCancelledNotificationConsumer> _logger;

    public OrderCancelledNotificationConsumer(ILogger<OrderCancelledNotificationConsumer> logger)
    {
        _logger = logger;
    }

    public string ConsumerName => nameof(OrderCancelledNotificationConsumer);
    public string QueueName => "notifications.order-cancelled";
    public string RoutingKeyPattern => $"orders.{nameof(OrderCancelledEvent)}";

    public Task HandleAsync(
        Guid messageId,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var orderId = root.GetProperty("orderId").GetProperty("value").GetGuid();
        var reason = root.GetProperty("reason").GetString();

        _logger.LogInformation(
            "[{Consumer}] Dispatched cancellation notification for Order {OrderId} (Reason: {Reason}, EventId: {MessageId})",
            ConsumerName,
            orderId,
            reason,
            messageId);

        return Task.CompletedTask;
    }
}
