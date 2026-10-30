namespace TradeFlow.Infrastructure.Messaging.Kafka;

public interface IMarketTradeStreamHandler
{
    Task HandleAsync(
        MarketTradeStreamEvent tradeEvent,
        int partition,
        long offset,
        CancellationToken cancellationToken = default);
}
