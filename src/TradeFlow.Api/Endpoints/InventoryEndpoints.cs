using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Api.Common.Security;
using TradeFlow.Application.Inventory.Commands.CreateInventoryItem;
using TradeFlow.Application.Inventory.Commands.RestockInventory;
using TradeFlow.Application.Inventory.Queries.GetInventoryItems;

namespace TradeFlow.Api.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inventory")
            .WithTags("Inventory");

        group.MapGet("/", GetInventoryItemsAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderReadPolicy)
            .WithName("GetInventoryItems")
            .WithSummary("Lists available inventory SKUs and stock quantities.")
            .Produces<IReadOnlyList<InventoryItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", CreateInventoryItemAsync)
            .RequireAuthorization(TradeFlowPolicies.AdminPolicy)
            .WithName("CreateInventoryItem")
            .WithSummary("Registers warehouse stock tracking for a product SKU (Admin only).")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{productId:guid}/restock", RestockInventoryAsync)
            .RequireAuthorization(TradeFlowPolicies.AdminPolicy)
            .WithName("RestockInventory")
            .WithSummary("Replenishes physical warehouse stock for an existing SKU (Admin only).")
            .Produces<InventoryItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetInventoryItemsAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetInventoryItemsQuery(), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateInventoryItemAsync(
        CreateInventoryItemRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateInventoryItemCommand(request.ProductId, request.Sku, request.InitialQuantity),
            cancellationToken);
        return result.ToHttpResult(id => $"/api/inventory/{request.ProductId}");
    }

    private static async Task<IResult> RestockInventoryAsync(
        Guid productId,
        RestockInventoryRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RestockInventoryCommand(productId, request.Quantity),
            cancellationToken);
        return result.ToHttpResult();
    }
}

public sealed record CreateInventoryItemRequest(Guid ProductId, string Sku, int InitialQuantity);
public sealed record RestockInventoryRequest(int Quantity);
