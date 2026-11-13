using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradeFlow.Application.Common.Caching;

namespace TradeFlow.Infrastructure.Caching;

/// <summary>
/// Distributed fixed-window rate limiter backed by Redis 7 using an atomic Lua script (<c>INCR</c> + <c>PEXPIRE</c>).
/// Ensures consistent quota enforcement across multiple Kubernetes pods while failing open to
/// <see cref="InMemoryDistributedRateLimiter"/> if Redis is temporarily unavailable (ADR-0016).
/// </summary>
public sealed class RedisDistributedRateLimiter : IDistributedRateLimiter
{
    private const string AtomicRateLimitLuaScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        return { current, ttl }
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly InMemoryDistributedRateLimiter _fallbackLimiter;
    private readonly ILogger<RedisDistributedRateLimiter> _logger;

    public RedisDistributedRateLimiter(
        IConnectionMultiplexer redis,
        ILogger<RedisDistributedRateLimiter> logger,
        TimeProvider? timeProvider = null)
    {
        _redis = redis;
        _logger = logger;
        _fallbackLimiter = new InMemoryDistributedRateLimiter(timeProvider);
    }

    public async Task<RateLimitLeaseResult> TryAcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);

        if (!_redis.IsConnected)
        {
            return await _fallbackLimiter.TryAcquireAsync(partitionKey, permitLimit, window, cancellationToken);
        }

        try
        {
            var db = _redis.GetDatabase();
            var redisKey = (RedisKey)$"ratelimit:{partitionKey}";
            var windowMs = (long)Math.Max(1000, window.TotalMilliseconds);

            var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
                AtomicRateLimitLuaScript,
                [redisKey],
                [windowMs]);

            if (result is { Length: 2 })
            {
                var currentCount = (long)result[0];
                var ttlMs = (long)result[1];
                var retryAfter = ttlMs > 0
                    ? TimeSpan.FromMilliseconds(ttlMs)
                    : window;

                return new RateLimitLeaseResult(
                    IsAllowed: currentCount <= permitLimit,
                    CurrentCount: currentCount,
                    PermitLimit: permitLimit,
                    RetryAfter: retryAfter);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[Redis RateLimiter Warning] Failed to evaluate atomic rate limit for '{PartitionKey}'. Falling back to in-memory limiter (Fail-Open).",
                partitionKey);
        }

        return await _fallbackLimiter.TryAcquireAsync(partitionKey, permitLimit, window, cancellationToken);
    }
}
