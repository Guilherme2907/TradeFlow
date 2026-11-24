using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Customers;
using TradeFlow.Domain.Customers.Exceptions;

namespace TradeFlow.Application.Customers.Commands.CreateCustomerAccount;

public sealed record CreateCustomerAccountCommand(
    string CompanyName,
    string TaxId,
    string Tier,
    decimal CreditLimit,
    string Currency = "USD",
    int PaymentTermsDays = 30,
    Guid? ExplicitCustomerId = null) : IRequest<Result<Guid>>;

public sealed class CreateCustomerAccountCommandValidator : AbstractValidator<CreateCustomerAccountCommand>
{
    public CreateCustomerAccountCommandValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TaxId).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Tier)
            .Must(t => Enum.TryParse<CustomerTier>(t, ignoreCase: true, out _))
            .WithMessage("Tier must be Standard, Gold, or Enterprise.");
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 180);
    }
}

public sealed class CreateCustomerAccountCommandHandler
    : IRequestHandler<CreateCustomerAccountCommand, Result<Guid>>
{
    private readonly ICustomerAccountRepository _repository;

    public CreateCustomerAccountCommandHandler(ICustomerAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Guid>> Handle(
        CreateCustomerAccountCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedTaxId = request.TaxId.Trim().ToUpperInvariant();
        var existing = await _repository.GetByTaxIdAsync(normalizedTaxId, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<Guid>(
                Error.Conflict("CUSTOMER_DUPLICATE_TAX_ID", $"Customer with Tax ID '{normalizedTaxId}' already exists."));
        }

        if (!Enum.TryParse<CustomerTier>(request.Tier, ignoreCase: true, out var tier))
        {
            return Result.Failure<Guid>(
                Error.Validation("CUSTOMER_INVALID_TIER", $"Invalid tier '{request.Tier}'."));
        }

        CustomerAccount account;
        try
        {
            account = CustomerAccount.Create(
                request.CompanyName,
                request.TaxId,
                tier,
                request.CreditLimit,
                request.Currency,
                request.PaymentTermsDays,
                request.ExplicitCustomerId.HasValue ? new CustomerAccountId(request.ExplicitCustomerId.Value) : null);
        }
        catch (CustomerInvariantException ex)
        {
            return Result.Failure<Guid>(Error.Validation(ex.Code, ex.Message));
        }

        await _repository.AddAsync(account, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success(account.Id.Value);
    }
}
