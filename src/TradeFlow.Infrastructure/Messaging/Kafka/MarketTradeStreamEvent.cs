using System.Text.Json;

namespace TradeFlow.Infrastructure.Messaging.Kafka;

/// <summary>
/// Immutable event record representing a trade execution on <see cref="KafkaTopology.TradesTopic"/>.
/// Partitioned by <see cref="ProductId"/> so all trades and price updates for the same financial instrument
/// are strictly ordered within the same partition (ADR-0012).
/// </summary>
public sealed record MarketTradeStreamEvent(
    Guid EventId,
    Guid OrderId,
    Guid ProductId,
    string Sku,
    decimal UnitPrice,
    string Currency,
    int Quantity,
    DateTimeOffset ExecutedAt)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Deserializes a trade event from either a direct Outbox domain event payload (<c>OrderLineAddedEvent</c>
    /// with nested Value Objects) or a flat <see cref="MarketTradeStreamEvent"/> JSON contract.
    /// Throws <see cref="JsonException"/> if the JSON is malformed/corrupted (Poison Pill).
    /// </summary>
    public static MarketTradeStreamEvent Deserialize(string rawPayload)
    {
        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;

        // Case 1: Outbox Domain Event JSON (nested Value Objects: orderId.value, productId.value, unitPrice.amount)
        if (root.TryGetProperty("unitPrice", out var unitPriceProp) && unitPriceProp.ValueKind == JsonValueKind.Object)
        {
            var eventId = root.TryGetProperty("eventId", out var eid) ? eid.GetGuid() : Guid.Empty;
            var orderId = root.GetProperty("orderId").GetProperty("value").GetGuid();
            var productId = root.GetProperty("productId").GetProperty("value").GetGuid();
            var sku = root.GetProperty("sku").GetString() ?? "UNKNOWN";
            var amount = unitPriceProp.GetProperty("amount").GetDecimal();
            var currency = unitPriceProp.GetProperty("currency").GetString() ?? "USD";
            var quantity = root.GetProperty("quantity").GetInt32();
            var occurredOn = root.TryGetProperty("occurredOn", out var occ)
                ? occ.GetDateTimeOffset()
                : DateTimeOffset.UtcNow;

            return new MarketTradeStreamEvent(
                EventId: eventId,
                OrderId: orderId,
                ProductId: productId,
                Sku: sku,
                UnitPrice: amount,
                Currency: currency,
                Quantity: quantity,
                ExecutedAt: occurredOn);
        }

        // Case 2: Flat MarketTradeStreamEvent JSON
        return JsonSerializer.Deserialize<MarketTradeStreamEvent>(rawPayload, SerializerOptions)
            ?? throw new JsonException("Deserialized MarketTradeStreamEvent was null.");
    }
}

/// <summary>
/// Metadata returned by the Kafka broker upon acknowledging a published stream record.
/// </summary>
public sealed record KafkaStreamDeliveryResult(
    string Topic,
    int Partition,
    long Offset,
    string Key);
