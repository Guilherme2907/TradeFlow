using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Api.Common.Security;
using TradeFlow.Application.Customers.Commands.AddCustomerContract;
using TradeFlow.Application.Customers.Commands.CreateCustomerAccount;
using TradeFlow.Application.Customers.Queries;
using TradeFlow.Application.Customers.Queries.GetCustomerAccounts;
using TradeFlow.Application.Customers.Queries.GetMyCustomerAccount;

namespace TradeFlow.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/customers")
            .WithTags("Customers");

        group.MapGet("/me", GetMyAccountAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderReadPolicy)
            .WithName("GetMyCustomerAccount")
            .WithSummary("Retrieves the authenticated trader's own corporate account profile, credit limit, and confidential contracts.")
            .Produces<CustomerAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", GetAllAccountsAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderReadPolicy)
            .WithName("GetCustomerAccounts")
            .WithSummary("Lists all B2B customer accounts and contracts (Admin, RiskManager, or ComplianceAuditor only).")
            .Produces<IReadOnlyList<CustomerAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateAccountAsync)
            .RequireAuthorization(TradeFlowPolicies.AdminPolicy)
            .WithName("CreateCustomerAccount")
            .WithSummary("Creates a new B2B customer account with credit limit and payment terms (Admin only).")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/contracts", AddContractAsync)
            .RequireAuthorization(TradeFlowPolicies.AdminPolicy)
            .WithName("AddCustomerContract")
            .WithSummary("Adds a confidential B2B commercial contract to a customer account (Admin only).")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetMyAccountAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMyCustomerAccountQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAllAccountsAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCustomerAccountsQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateCustomerAccountRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateCustomerAccountCommand(
            request.CompanyName,
            request.TaxId,
            request.Tier,
            request.CreditLimit,
            request.Currency ?? "USD",
            request.PaymentTermsDays ?? 30,
            request.ExplicitCustomerId);

        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(id => $"/api/customers/{id}");
    }

    private static async Task<IResult> AddContractAsync(
        Guid id,
        AddCustomerContractRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new AddCustomerContractCommand(
            id,
            request.ContractNumber,
            request.Title,
            request.TermsText,
            request.DiscountPercent,
            request.EffectiveFrom,
            request.ExpiresAt);

        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(contractId => $"/api/customers/{id}/contracts/{contractId}");
    }
}

public sealed record CreateCustomerAccountRequest(
    string CompanyName,
    string TaxId,
    string Tier,
    decimal CreditLimit,
    string? Currency = "USD",
    int? PaymentTermsDays = 30,
    Guid? ExplicitCustomerId = null);

public sealed record AddCustomerContractRequest(
    string ContractNumber,
    string Title,
    string TermsText,
    decimal DiscountPercent,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset ExpiresAt);
