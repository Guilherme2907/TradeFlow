using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Api.Common.Security;
using TradeFlow.Application.Orders.Commands.AddOrderLine;
using TradeFlow.Application.Orders.Commands.CancelOrder;
using TradeFlow.Application.Orders.Commands.CreateOrder;
using TradeFlow.Application.Orders.Commands.SubmitOrder;

namespace TradeFlow.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders");

        group.MapPost("/", CreateOrderAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderWritePolicy)
            .WithName("CreateOrder")
            .WithSummary("Creates a new draft B2B purchase order for the authenticated trader's customer tenant.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/lines", AddOrderLineAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderWritePolicy)
            .WithName("AddOrderLine")
            .WithSummary("Adds a commercial product line snapshot to an existing draft order.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/submit", SubmitOrderAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderWritePolicy)
            .WithName("SubmitOrder")
            .WithSummary("Submits a draft order and executes atomic inventory stock reservation.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/cancel", CancelOrderAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderCancelPolicy)
            .WithName("CancelOrder")
            .WithSummary("Cancels an order (allowed for the owning Trader or cross-tenant by RiskManager/Admin).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(request.Currency, request.MinimumOrderValue);
        var result = await sender.Send(command, cancellationToken);

        return result.ToHttpResult(id => $"/api/orders/{id}");
    }

    private static async Task<IResult> AddOrderLineAsync(
        Guid id,
        AddOrderLineRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new AddOrderLineCommand(
            id,
            request.ProductId,
            request.Sku,
            request.ProductName,
            request.UnitPrice,
            request.Quantity);

        var result = await sender.Send(command, cancellationToken);

        return result.ToHttpResult(lineId => $"/api/orders/{id}/lines/{lineId}");
    }

    private static async Task<IResult> SubmitOrderAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new SubmitOrderCommand(id);
        var result = await sender.Send(command, cancellationToken);

        return result.ToHttpResult();
    }

    private static async Task<IResult> CancelOrderAsync(
        Guid id,
        CancelOrderRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CancelOrderCommand(id, request.Reason);
        var result = await sender.Send(command, cancellationToken);

        return result.ToHttpResult();
    }
}

/// <summary>
/// ADR-0013: <c>CustomerId</c> is never accepted from the HTTP body; it is resolved from the signed JWT <c>customer_id</c> claim.
/// </summary>
public sealed record CreateOrderRequest(
    string Currency,
    decimal? MinimumOrderValue = null);

public sealed record AddOrderLineRequest(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record CancelOrderRequest(
    string Reason);
