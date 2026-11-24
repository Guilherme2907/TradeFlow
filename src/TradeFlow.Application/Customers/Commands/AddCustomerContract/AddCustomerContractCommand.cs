using FluentValidation;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Customers;
using TradeFlow.Domain.Customers.Exceptions;

namespace TradeFlow.Application.Customers.Commands.AddCustomerContract;

public sealed record AddCustomerContractCommand(
    Guid CustomerId,
    string ContractNumber,
    string Title,
    string TermsText,
    decimal DiscountPercent,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset ExpiresAt) : IRequest<Result<Guid>>;

public sealed class AddCustomerContractCommandValidator : AbstractValidator<AddCustomerContractCommand>
{
    public AddCustomerContractCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ContractNumber).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TermsText).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0m, 50m);
        RuleFor(x => x.ExpiresAt).GreaterThan(x => x.EffectiveFrom);
    }
}

public sealed class AddCustomerContractCommandHandler
    : IRequestHandler<AddCustomerContractCommand, Result<Guid>>
{
    private readonly ICustomerAccountRepository _repository;

    public AddCustomerContractCommandHandler(ICustomerAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<Guid>> Handle(
        AddCustomerContractCommand request,
        CancellationToken cancellationToken)
    {
        var account = await _repository.GetByIdAsync(new CustomerAccountId(request.CustomerId), cancellationToken);
        if (account is null)
        {
            return Result.Failure<Guid>(
                Error.NotFound("CUSTOMER_NOT_FOUND", $"Customer '{request.CustomerId}' was not found."));
        }

        CustomerContract contract;
        try
        {
            contract = account.AddContract(
                request.ContractNumber,
                request.Title,
                request.TermsText,
                request.DiscountPercent,
                request.EffectiveFrom,
                request.ExpiresAt);
        }
        catch (CustomerInvariantException ex)
        {
            var error = ex.Code == "CONTRACT_DUPLICATE_NUMBER"
                ? Error.Conflict(ex.Code, ex.Message)
                : Error.Validation(ex.Code, ex.Message);
            return Result.Failure<Guid>(error);
        }

        _repository.Update(account);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result.Success(contract.Id.Value);
    }
}
