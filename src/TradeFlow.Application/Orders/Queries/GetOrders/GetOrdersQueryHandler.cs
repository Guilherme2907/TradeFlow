using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Queries.GetOrderById;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Queries.GetOrders;

public sealed class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, Result<IReadOnlyList<OrderResponse>>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUserContext _currentUser;

    public GetOrdersQueryHandler(
        IOrderRepository orderRepository,
        ICurrentUserContext currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<OrderResponse>>> Handle(
        GetOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var isPrivilegedRole = _currentUser.IsInRole(TradeFlowRoles.Admin) ||
                               _currentUser.IsInRole(TradeFlowRoles.ComplianceAuditor) ||
                               _currentUser.IsInRole(TradeFlowRoles.RiskManager);

        CustomerId? effectiveCustomerId;

        if (isPrivilegedRole)
        {
            effectiveCustomerId = request.FilterCustomerId.HasValue
                ? new CustomerId(request.FilterCustomerId.Value)
                : null;
        }
        else
        {
            if (_currentUser.CustomerId is null)
            {
                return Result.Failure<IReadOnlyList<OrderResponse>>(
                    Error.Forbidden("ORDER_TENANT_MISSING", "Authenticated trader token is missing the required customer_id claim."));
            }

            if (request.FilterCustomerId.HasValue && request.FilterCustomerId.Value != _currentUser.CustomerId.Value)
            {
                return Result.Failure<IReadOnlyList<OrderResponse>>(
                    Error.Forbidden("ORDER_TENANT_FORBIDDEN", "You do not have permission to list orders from another customer tenant."));
            }

            effectiveCustomerId = new CustomerId(_currentUser.CustomerId.Value);
        }

        var orders = await _orderRepository.ListAsync(effectiveCustomerId, cancellationToken);
        IReadOnlyList<OrderResponse> response = orders.Select(o => o.ToResponse()).ToList();

        return Result.Success(response);
    }
}
