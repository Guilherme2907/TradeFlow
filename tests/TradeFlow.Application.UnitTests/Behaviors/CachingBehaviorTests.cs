using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeFlow.Application.Common.Behaviors;
using TradeFlow.Application.Common.Caching;
using TradeFlow.Application.Common.Models;
using Xunit;

namespace TradeFlow.Application.UnitTests.Behaviors;

public class CachingBehaviorTests
{
    public sealed record NonCachedCommand(string Name) : IRequest<Result<string>>;

    public sealed record CachedQuery(string Id) : IRequest<Result<string>>, ICacheableQuery
    {
        public string CacheKey => $"items:{Id}";
        public TimeSpan? Expiration => TimeSpan.FromMinutes(2);
    }

    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();

    [Fact]
    public async Task Handle_WhenRequestIsNotCacheableQuery_ShouldInvokeNextDirectlyWithoutCacheService()
    {
        // Arrange
        var behavior = new CachingBehavior<NonCachedCommand, Result<string>>(
            _cacheService,
            NullLogger<CachingBehavior<NonCachedCommand, Result<string>>>.Instance);

        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();
        next.Invoke().Returns(Result.Success("PassThrough"));

        var command = new NonCachedCommand("Test");

        // Act
        var result = await behavior.Handle(command, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("PassThrough");
        await next.Received(1).Invoke();
        await _cacheService.DidNotReceiveWithAnyArgs().GetOrCreateAsync<Result<string>>(
            default!, default!, default, default, default);
    }

    [Fact]
    public async Task Handle_WhenRequestIsCacheableQuery_ShouldDelegateToCacheServiceGetOrCreate()
    {
        // Arrange
        var behavior = new CachingBehavior<CachedQuery, Result<string>>(
            _cacheService,
            NullLogger<CachingBehavior<CachedQuery, Result<string>>>.Instance);

        var query = new CachedQuery("item-123");
        var expectedResponse = Result.Success("CachedValue");

        _cacheService.GetOrCreateAsync(
            Arg.Is("items:item-123"),
            Arg.Any<Func<CancellationToken, Task<Result<string>>>>(),
            Arg.Is(TimeSpan.FromMinutes(2)),
            Arg.Any<Func<Result<string>, bool>>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();

        // Act
        var result = await behavior.Handle(query, next, CancellationToken.None);

        // Assert
        result.Should().Be(expectedResponse);
        await _cacheService.Received(1).GetOrCreateAsync(
            Arg.Is("items:item-123"),
            Arg.Any<Func<CancellationToken, Task<Result<string>>>>(),
            Arg.Is(TimeSpan.FromMinutes(2)),
            Arg.Any<Func<Result<string>, bool>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFactoryIsInvokedOnCacheMiss_ShouldExecuteInnerHandler()
    {
        // Arrange
        var behavior = new CachingBehavior<CachedQuery, Result<string>>(
            _cacheService,
            NullLogger<CachingBehavior<CachedQuery, Result<string>>>.Instance);

        var query = new CachedQuery("item-456");
        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();
        next.Invoke().Returns(Result.Success("FreshFromDb"));

        _cacheService.GetOrCreateAsync<Result<string>>(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<Result<string>>>>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<Func<Result<string>, bool>>(),
            Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var factory = callInfo.Arg<Func<CancellationToken, Task<Result<string>>>>();
                return (Result<string>?)await factory(CancellationToken.None);
            });

        // Act
        var result = await behavior.Handle(query, next, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("FreshFromDb");
        await next.Received(1).Invoke();
    }
}
