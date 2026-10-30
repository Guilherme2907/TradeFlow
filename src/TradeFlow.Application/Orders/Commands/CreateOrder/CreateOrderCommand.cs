using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Application.Orders.Gateways;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Creates a new draft B2B purchase order.
/// ADR-0013: <c>CustomerId</c> is intentionally excluded from the command payload and resolved exclusively
/// from <see cref="ICurrentUserContext.CustomerId"/> (signed JWT claim) to eliminate BOLA/IDOR spoofing.
/// </summary>
public sealed record CreateOrderCommand(
    string Currency,
    decimal? MinimumOrderValue = null) : IRequest<Result<Guid>>;

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a valid 3-letter ISO code.");

        RuleFor(x => x.MinimumOrderValue)
            .GreaterThan(0)
            .When(x => x.MinimumOrderValue.HasValue)
            .WithMessage("MinimumOrderValue must be greater than zero when specified.");
    }
}

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserContext _currentUser;

    public CreateOrderCommandHandler(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ICurrentUserContext currentUser)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.CustomerId is null || _currentUser.CustomerId.Value == Guid.Empty)
        {
            return Result.Failure<Guid>(Error.Forbidden(
                "CUSTOMER_TENANT_REQUIRED",
                "The authenticated user does not have a valid B2B 'customer_id' tenant claim."));
        }

        var customerId = new CustomerId(_currentUser.CustomerId.Value);
        Money? minimumOrderValue = request.MinimumOrderValue.HasValue
            ? Money.Create(request.MinimumOrderValue.Value, request.Currency)
            : null;

        var order = Order.Create(customerId, request.Currency, minimumOrderValue);

        await _orderRepository.AddAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(order.Id.Value);
    }
}
