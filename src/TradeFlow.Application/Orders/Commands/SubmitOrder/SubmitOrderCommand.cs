using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Exceptions;

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

    public SubmitOrderCommandHandler(
        IOrderRepository orderRepository,
        IInventoryReservationService inventoryService,
        IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _inventoryService = inventoryService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SubmitOrderCommand request, CancellationToken cancellationToken)
    {
        // ADR-0010 retry contract: the delegate may be re-executed after a transient failure, so every
        // aggregate it mutates is loaded inside it (the UnitOfWork clears trackers between attempts).
        // Only non-locking reads precede the SELECT FOR UPDATE, keeping lock duration minimal.
        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var order = await _orderRepository.GetByIdAsync(new OrderId(request.OrderId), cancellationToken);
            if (order is null)
            {
                return Result.Failure(Error.NotFound("ORDER_NOT_FOUND", $"Order '{request.OrderId}' was not found."));
            }

            // Verify domain invariants before attempting any reservation (fail fast, no locks taken yet)
            order.ValidateCanSubmit();

            var reservationItems = order.Lines
                .Select(line => new ReservationItem(line.ProductId, line.Sku, line.Quantity))
                .ToList();

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
