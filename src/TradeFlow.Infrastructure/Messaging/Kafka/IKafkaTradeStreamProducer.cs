namespace TradeFlow.Infrastructure.Messaging.Kafka;

public interface IKafkaTradeStreamProducer : IDisposable
{
    Task<KafkaStreamDeliveryResult> PublishTradeAsync(
        MarketTradeStreamEvent tradeEvent,
        CancellationToken cancellationToken = default);

    Task<KafkaStreamDeliveryResult> PublishRawAsync(
        string topic,
        string key,
        string rawPayload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
