using MediatR;
using Microsoft.Extensions.Logging;
using TradeFlow.Application.Common.Caching;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Common.Behaviors;

/// <summary>
/// Cross-cutting MediatR pipeline interceptor that executes the Cache-Aside pattern with single-flight
/// stampede protection for queries implementing <see cref="ICacheableQuery"/> (ADR-0014).
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICacheService _cacheService;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(
        ICacheService cacheService,
        ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheableQuery cacheableQuery)
        {
            return await next();
        }

        var key = cacheableQuery.CacheKey;

        var response = await _cacheService.GetOrCreateAsync(
            key,
            async _ =>
            {
                _logger.LogDebug("[Cache Miss] Key '{CacheKey}' executing query handler...", key);
                return await next();
            },
            cacheableQuery.Expiration,
            shouldCache: res => res is not Result { IsFailure: true },
            cancellationToken: cancellationToken);

        return response!;
    }
}
