using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Infrastructure.Messaging.Kafka;

/// <summary>
/// Processes incoming <see cref="MarketTradeStreamEvent"/> records from the Kafka trade stream,
/// maintaining a bounded sliding window of the most recent executions per ProductId (ADR-0012).
/// </summary>
public sealed class MarketTradeAnalyticsHandler : IMarketTradeStreamHandler
{
    public const int DefaultMaxHistoryPerProduct = 100;

    private readonly ConcurrentDictionary<Guid, List<MarketTradeStreamEvent>> _historyByProduct = new();
    private readonly ILogger<MarketTradeAnalyticsHandler> _logger;
    private readonly int _maxHistoryPerProduct;

    public MarketTradeAnalyticsHandler(
        ILogger<MarketTradeAnalyticsHandler> logger,
        int maxHistoryPerProduct = DefaultMaxHistoryPerProduct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHistoryPerProduct);
        _logger = logger;
        _maxHistoryPerProduct = maxHistoryPerProduct;
    }

    public Task HandleAsync(
        MarketTradeStreamEvent tradeEvent,
        int partition,
        long offset,
        CancellationToken cancellationToken = default)
    {
        if (tradeEvent.UnitPrice <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid market trade unit price ({tradeEvent.UnitPrice}) for SKU '{tradeEvent.Sku}'.");
        }

        if (tradeEvent.Quantity <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid market trade quantity ({tradeEvent.Quantity}) for SKU '{tradeEvent.Sku}'.");
        }

        var history = _historyByProduct.GetOrAdd(tradeEvent.ProductId, _ => []);
        lock (history)
        {
            if (history.Count >= _maxHistoryPerProduct)
            {
                history.RemoveAt(0);
            }

            history.Add(tradeEvent);
        }

        _logger.LogInformation(
            "[Kafka MarketAnalytics] Processed trade for {Sku} (ProductId: {ProductId}) at {UnitPrice} {Currency} x {Quantity} [Partition: {Partition}, Offset: {Offset}]",
            tradeEvent.Sku,
            tradeEvent.ProductId,
            tradeEvent.UnitPrice,
            tradeEvent.Currency,
            tradeEvent.Quantity,
            partition,
            offset);

        return Task.CompletedTask;
    }

    public IReadOnlyList<MarketTradeStreamEvent> GetProcessedHistory(Guid productId)
    {
        if (!_historyByProduct.TryGetValue(productId, out var history))
        {
            return [];
        }

        lock (history)
        {
            return history.ToList();
        }
    }
}
