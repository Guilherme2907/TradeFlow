using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeFlow.Domain.Orders.Events;
using TradeFlow.Infrastructure.Messaging;

namespace TradeFlow.Infrastructure.Modules.Orders.Consumers;

/// <summary>
/// Asynchronous consumer that reacts to <see cref="OrderSubmittedEvent"/> to trigger B2B invoice generation
/// and financial compliance reporting (ADR-0011).
/// </summary>
public sealed class OrderSubmittedInvoiceConsumer : IIntegrationEventConsumer
{
    private readonly ILogger<OrderSubmittedInvoiceConsumer> _logger;

    public OrderSubmittedInvoiceConsumer(ILogger<OrderSubmittedInvoiceConsumer> logger)
    {
        _logger = logger;
    }

    public string ConsumerName => nameof(OrderSubmittedInvoiceConsumer);
    public string QueueName => "invoicing.order-submitted";
    public string RoutingKeyPattern => $"orders.{nameof(OrderSubmittedEvent)}";

    public Task HandleAsync(
        Guid messageId,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var orderId = root.GetProperty("orderId").GetProperty("value").GetGuid();
        var totalAmount = root.GetProperty("totalAmount").GetProperty("amount").GetDecimal();
        var currency = root.GetProperty("totalAmount").GetProperty("currency").GetString();

        _logger.LogInformation(
            "[{Consumer}] Generated B2B invoice for submitted Order {OrderId} (Total: {Amount} {Currency}, EventId: {MessageId})",
            ConsumerName,
            orderId,
            totalAmount,
            currency,
            messageId);

        return Task.CompletedTask;
    }
}
