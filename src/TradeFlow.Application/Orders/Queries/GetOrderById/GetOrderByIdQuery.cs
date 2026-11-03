using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Caching;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Orders.Queries.GetOrderById;

/// <summary>
/// Retrieves an order by identifier.
/// Implements <see cref="ICacheableQuery"/> to leverage distributed caching with Cache Stampede protection (ADR-0014).
/// Cache key incorporates the tenant identifier to prevent cross-tenant cache pollution.
/// </summary>
public sealed record GetOrderByIdQuery(
    Guid OrderId,
    Guid? TenantCustomerId = null) : IRequest<Result<OrderResponse>>, ICacheableQuery
{
    public string CacheKey => TenantCustomerId.HasValue
        ? $"orders:{OrderId}:tenant:{TenantCustomerId.Value}"
        : $"orders:{OrderId}:tenant:global";

    public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
}

public sealed class GetOrderByIdQueryValidator : AbstractValidator<GetOrderByIdQuery>
{
    public GetOrderByIdQueryValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("OrderId is required.");
    }
}

public sealed record OrderResponse(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    string Currency,
    decimal TotalAmount,
    IReadOnlyList<OrderLineResponse> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt = null,
    DateTimeOffset? CancelledAt = null,
    string? CancellationReason = null);

public sealed record OrderLineResponse(
    Guid LineId,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    string Currency,
    int Quantity,
    decimal Total);


