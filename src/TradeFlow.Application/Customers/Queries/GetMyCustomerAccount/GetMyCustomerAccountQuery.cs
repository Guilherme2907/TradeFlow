using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Domain.Customers;

namespace TradeFlow.Application.Customers.Queries.GetMyCustomerAccount;

public sealed record GetMyCustomerAccountQuery : IRequest<Result<CustomerAccountResponse>>;

public sealed class GetMyCustomerAccountQueryHandler
    : IRequestHandler<GetMyCustomerAccountQuery, Result<CustomerAccountResponse>>
{
    private readonly ICustomerAccountRepository _repository;
    private readonly ICurrentUserContext _currentUser;

    public GetMyCustomerAccountQueryHandler(
        ICustomerAccountRepository repository,
        ICurrentUserContext currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerAccountResponse>> Handle(
        GetMyCustomerAccountQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.CustomerId is null)
        {
            return Result.Failure<CustomerAccountResponse>(
                Error.Forbidden("CUSTOMER_TENANT_MISSING", "Authenticated user token does not contain a customer_id claim."));
        }

        var account = await _repository.GetByIdAsync(
            new CustomerAccountId(_currentUser.CustomerId.Value),
            cancellationToken);

        if (account is null)
        {
            return Result.Failure<CustomerAccountResponse>(
                Error.NotFound("CUSTOMER_NOT_FOUND", $"Customer account '{_currentUser.CustomerId.Value}' was not found."));
        }

        return Result.Success(account.ToResponse());
    }
}
