using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Queries.GetOrderById;

namespace TradeFlow.Application.Orders.Queries.GetOrders;

/// <summary>
/// Lists orders accessible to the current user context.
/// Standard traders are restricted strictly to their own tenant; privileged roles may inspect across tenants (ADR-0013).
/// </summary>
public sealed record GetOrdersQuery(
    Guid? FilterCustomerId = null,
    string? Status = null,
    string? Currency = null) : IRequest<Result<IReadOnlyList<OrderResponse>>>;
