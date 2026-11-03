using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Queries.GetOrderById;

/// <summary>
/// Query handler executing the database read for an order.
/// Remains 100% pure: does not know about Redis, serialization, or caching infrastructure.
/// Caching is handled transparently via <see cref="Common.Behaviors.CachingBehavior{TRequest, TResponse}"/> (ADR-0014).
/// </summary>
public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, Result<OrderResponse>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUserContext _currentUser;

    public GetOrderByIdQueryHandler(
        IOrderRepository orderRepository,
        ICurrentUserContext currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<OrderResponse>> Handle(
        GetOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(request.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure<OrderResponse>(
                Error.NotFound("ORDER_NOT_FOUND", $"Order '{request.OrderId}' was not found."));
        }

        // ADR-0013: Tenant Isolation — cross-tenant reading is restricted to Admin, RiskManager, and ComplianceAuditor
        var isPrivilegedRole = _currentUser.IsInRole(TradeFlowRoles.Admin) ||
                               _currentUser.IsInRole(TradeFlowRoles.ComplianceAuditor) ||
                               _currentUser.IsInRole(TradeFlowRoles.RiskManager);

        if (!isPrivilegedRole &&
            (_currentUser.CustomerId is null || order.CustomerId.Value != _currentUser.CustomerId.Value))
        {
            return Result.Failure<OrderResponse>(
                Error.Forbidden("ORDER_TENANT_FORBIDDEN", "You do not have permission to access orders from another customer tenant."));
        }

        var lines = order.Lines.Select(l => new OrderLineResponse(
            LineId: l.Id.Value,
            ProductId: l.ProductId.Value,
            Sku: l.Sku,
            ProductName: l.ProductName,
            UnitPrice: l.UnitPrice.Amount,
            Currency: l.UnitPrice.Currency,
            Quantity: l.Quantity,
            Total: l.LineTotal.Amount)).ToList();

        var response = new OrderResponse(
            OrderId: order.Id.Value,
            CustomerId: order.CustomerId.Value,
            Status: order.Status.ToString(),
            Currency: order.Currency,
            TotalAmount: order.TotalAmount.Amount,
            Lines: lines,
            CreatedAt: order.CreatedAt,
            SubmittedAt: order.SubmittedAt,
            CancelledAt: order.CancelledAt,
            CancellationReason: order.CancellationReason);

        return Result.Success(response);
    }
}
