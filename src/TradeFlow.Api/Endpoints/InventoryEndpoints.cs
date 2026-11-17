using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Api.Common.Security;
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

        return app;
    }

    private static async Task<IResult> GetInventoryItemsAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetInventoryItemsQuery(), cancellationToken);
        return result.ToHttpResult();
    }
}
