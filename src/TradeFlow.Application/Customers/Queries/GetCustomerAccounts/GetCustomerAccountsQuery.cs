using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Domain.Customers;

namespace TradeFlow.Application.Customers.Queries.GetCustomerAccounts;

public sealed record GetCustomerAccountsQuery : IRequest<Result<IReadOnlyList<CustomerAccountResponse>>>;

public sealed class GetCustomerAccountsQueryHandler
    : IRequestHandler<GetCustomerAccountsQuery, Result<IReadOnlyList<CustomerAccountResponse>>>
{
    private readonly ICustomerAccountRepository _repository;
    private readonly ICurrentUserContext _currentUser;

    public GetCustomerAccountsQueryHandler(
        ICustomerAccountRepository repository,
        ICurrentUserContext currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<CustomerAccountResponse>>> Handle(
        GetCustomerAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var isPrivileged = _currentUser.IsInRole(TradeFlowRoles.Admin) ||
                           _currentUser.IsInRole(TradeFlowRoles.RiskManager) ||
                           _currentUser.IsInRole(TradeFlowRoles.ComplianceAuditor);

        if (!isPrivileged)
        {
            return Result.Failure<IReadOnlyList<CustomerAccountResponse>>(
                Error.Forbidden("CUSTOMER_LIST_FORBIDDEN", "Only privileged roles (Admin, RiskManager, ComplianceAuditor) may list all customer accounts."));
        }

        var accounts = await _repository.ListAllAsync(cancellationToken);
        IReadOnlyList<CustomerAccountResponse> response = accounts.Select(a => a.ToResponse()).ToList();
        return Result.Success(response);
    }
}
