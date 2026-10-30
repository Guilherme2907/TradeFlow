using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Commands.SubmitOrder;

public sealed record SubmitOrderCommand(Guid OrderId) : IRequest<Result>;

public sealed class SubmitOrderCommandValidator : AbstractValidator<SubmitOrderCommand>
{
    public SubmitOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId is required.");
    }
}

public sealed class SubmitOrderCommandHandler : IRequestHandler<SubmitOrderCommand, Result>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryReservationService _inventoryService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserContext _currentUser;

    public SubmitOrderCommandHandler(
        IOrderRepository orderRepository,
        IInventoryReservationService inventoryService,
        IUnitOfWork unitOfWork,
        ICurrentUserContext currentUser)
    {
        _orderRepository = orderRepository;
        _inventoryService = inventoryService;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SubmitOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(request.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ORDER_NOT_FOUND", $"Order '{request.OrderId}' was not found."));
        }

        // ADR-0013: Prevent BOLA/IDOR — callers may only submit orders belonging to their own CustomerId tenant
        if (!_currentUser.IsInRole(TradeFlowRoles.Admin) &&
            (_currentUser.CustomerId is null || order.CustomerId.Value != _currentUser.CustomerId.Value))
        {
            return Result.Failure(Error.Forbidden(
                "ORDER_TENANT_FORBIDDEN",
                "You do not have permission to submit an order belonging to another B2B customer tenant."));
        }

        // Verify domain invariants upfront outside the transaction (Narrow Transaction Principle: fail fast with 0 locks)
        order.ValidateCanSubmit();

        var reservationItems = order.Lines
            .Select(line => new ReservationItem(line.ProductId, line.Sku, line.Quantity))
            .ToList();

        // Fail-fast narrow transaction: open transaction solely to acquire row locks, mutate state, and commit
        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var reservationResult = await _inventoryService.ReserveStockAsync(order.Id, reservationItems, cancellationToken);
            if (reservationResult.IsFailure)
            {
                return Result.Failure(reservationResult.Error);
            }

            // Finalize state transition to Submitted
            order.Submit();
            _orderRepository.Update(order);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }, cancellationToken);
    }
}
