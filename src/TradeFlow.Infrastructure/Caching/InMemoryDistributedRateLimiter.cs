using System.Collections.Concurrent;
using TradeFlow.Application.Common.Caching;

namespace TradeFlow.Infrastructure.Caching;

/// <summary>
/// Thread-safe in-memory fixed-window rate limiter used for local single-node environments
/// and as the automatic Fail-Open fallback when Redis is temporarily unreachable (ADR-0016).
/// </summary>
public sealed class InMemoryDistributedRateLimiter : IDistributedRateLimiter
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, PartitionWindow> _windows = new(StringComparer.Ordinal);

    public InMemoryDistributedRateLimiter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<RateLimitLeaseResult> TryAcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);

        var now = _timeProvider.GetUtcNow();
        var bucket = _windows.GetOrAdd(partitionKey, _ => new PartitionWindow(now.Add(window)));

        lock (bucket.SyncRoot)
        {
            if (now >= bucket.ExpiresAt)
            {
                bucket.Count = 0;
                bucket.ExpiresAt = now.Add(window);
            }

            bucket.Count++;
            var isAllowed = bucket.Count <= permitLimit;
            var remaining = bucket.ExpiresAt - now;
            if (remaining < TimeSpan.FromSeconds(1))
            {
                remaining = TimeSpan.FromSeconds(1);
            }

            return Task.FromResult(new RateLimitLeaseResult(
                IsAllowed: isAllowed,
                CurrentCount: bucket.Count,
                PermitLimit: permitLimit,
                RetryAfter: remaining));
        }
    }

    private sealed class PartitionWindow(DateTimeOffset expiresAt)
    {
        public object SyncRoot { get; } = new();
        public long Count { get; set; }
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    }
}
