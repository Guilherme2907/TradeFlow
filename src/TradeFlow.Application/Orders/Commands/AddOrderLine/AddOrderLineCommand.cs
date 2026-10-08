using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;
using TradeFlow.Domain.Orders.Exceptions;

namespace TradeFlow.Application.Orders.Commands.AddOrderLine;

public sealed record AddOrderLineCommand(
    Guid OrderId,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    int Quantity) : IRequest<Result<Guid>>;

public sealed class AddOrderLineCommandValidator : AbstractValidator<AddOrderLineCommand>
{
    public AddOrderLineCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId is required.");
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("ProductId is required.");
        RuleFor(x => x.Sku).NotEmpty().WithMessage("SKU snapshot is required.");
        RuleFor(x => x.ProductName).NotEmpty().WithMessage("Product name snapshot is required.");
        RuleFor(x => x.UnitPrice).GreaterThan(0).WithMessage("UnitPrice must be greater than zero.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}

public sealed class AddOrderLineCommandHandler : IRequestHandler<AddOrderLineCommand, Result<Guid>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AddOrderLineCommandHandler(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(request.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure<Guid>(Error.NotFound("ORDER_NOT_FOUND", $"Order '{request.OrderId}' was not found."));
        }

        var unitPrice = Money.Create(request.UnitPrice, order.Currency);
        var lineId = order.AddLine(
            new ProductId(request.ProductId),
            request.Sku,
            request.ProductName,
            unitPrice,
            request.Quantity);

        _orderRepository.Update(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(lineId.Value);
    }
}
