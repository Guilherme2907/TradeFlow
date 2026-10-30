using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Commands.CancelOrder;

public sealed record CancelOrderCommand(
    Guid OrderId,
    string Reason) : IRequest<Result>;

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Cancellation reason is required.");
    }
}

public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Result>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserContext _currentUser;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserContext currentUser)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(request.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ORDER_NOT_FOUND", $"Order '{request.OrderId}' was not found."));
        }

        // ADR-0013: RiskManager and Admin can execute emergency cancellations across tenants;
        // a regular Trader can only cancel orders belonging to their own CustomerId tenant.
        var canCancelAcrossTenants = _currentUser.IsInRole(TradeFlowRoles.RiskManager) ||
                                     _currentUser.IsInRole(TradeFlowRoles.Admin);

        if (!canCancelAcrossTenants &&
            (_currentUser.CustomerId is null || order.CustomerId.Value != _currentUser.CustomerId.Value))
        {
            return Result.Failure(Error.Forbidden(
                "ORDER_TENANT_FORBIDDEN",
                "You do not have permission to cancel an order belonging to another B2B customer tenant."));
        }

        order.Cancel(request.Reason);

        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
