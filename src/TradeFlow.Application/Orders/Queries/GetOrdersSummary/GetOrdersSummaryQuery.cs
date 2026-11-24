using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Queries.GetOrdersSummary;

public sealed record CurrencyVolumeSummary(string Currency, int OrderCount, decimal TotalAmount);

public sealed record OrdersSummaryResponse(
    int TotalOrders,
    int DraftCount,
    int SubmittedCount,
    int CancelledCount,
    IReadOnlyList<CurrencyVolumeSummary> SubmittedVolumeByCurrency);

public sealed record GetOrdersSummaryQuery(Guid? FilterCustomerId = null)
    : IRequest<Result<OrdersSummaryResponse>>;

public sealed class GetOrdersSummaryQueryHandler
    : IRequestHandler<GetOrdersSummaryQuery, Result<OrdersSummaryResponse>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUserContext _currentUser;

    public GetOrdersSummaryQueryHandler(
        IOrderRepository orderRepository,
        ICurrentUserContext currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<Result<OrdersSummaryResponse>> Handle(
        GetOrdersSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var isPrivileged = _currentUser.IsInRole(TradeFlowRoles.Admin) ||
                           _currentUser.IsInRole(TradeFlowRoles.ComplianceAuditor) ||
                           _currentUser.IsInRole(TradeFlowRoles.RiskManager);

        CustomerId? effectiveCustomerId;
        if (isPrivileged)
        {
            effectiveCustomerId = request.FilterCustomerId.HasValue
                ? new CustomerId(request.FilterCustomerId.Value)
                : null;
        }
        else
        {
            if (_currentUser.CustomerId is null)
            {
                return Result.Failure<OrdersSummaryResponse>(
                    Error.Forbidden("ORDER_TENANT_MISSING", "Authenticated trader token is missing the required customer_id claim."));
            }

            if (request.FilterCustomerId.HasValue && request.FilterCustomerId.Value != _currentUser.CustomerId.Value)
            {
                return Result.Failure<OrdersSummaryResponse>(
                    Error.Forbidden("ORDER_TENANT_FORBIDDEN", "You do not have permission to inspect summaries for another tenant."));
            }

            effectiveCustomerId = new CustomerId(_currentUser.CustomerId.Value);
        }

        var orders = await _orderRepository.ListAsync(effectiveCustomerId, cancellationToken);

        var submittedByCurrency = orders
            .Where(o => o.Status == OrderStatus.Submitted)
            .GroupBy(o => o.Currency)
            .Select(g => new CurrencyVolumeSummary(
                g.Key,
                g.Count(),
                g.Sum(x => x.TotalAmount.Amount)))
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        var summary = new OrdersSummaryResponse(
            orders.Count,
            orders.Count(o => o.Status == OrderStatus.Draft),
            orders.Count(o => o.Status == OrderStatus.Submitted),
            orders.Count(o => o.Status == OrderStatus.Cancelled),
            submittedByCurrency);

        return Result.Success(summary);
    }
}
