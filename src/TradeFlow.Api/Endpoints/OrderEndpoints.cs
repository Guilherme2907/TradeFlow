using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Application.Orders.Commands.AddOrderLine;
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
            .WithName("CreateOrder")
            .WithSummary("Creates a new draft B2B purchase order.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/lines", AddOrderLineAsync)
            .WithName("AddOrderLine")
            .WithSummary("Adds a commercial product line snapshot to an existing draft order.")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/submit", SubmitOrderAsync)
            .WithName("SubmitOrder")
            .WithSummary("Submits a draft order and executes atomic inventory stock reservation.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(request.CustomerId, request.Currency, request.MinimumOrderValue);
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
}

public sealed record CreateOrderRequest(
    Guid CustomerId,
    string Currency,
    decimal? MinimumOrderValue = null);

public sealed record AddOrderLineRequest(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    int Quantity);
