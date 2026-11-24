using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Api.Common.Security;
using TradeFlow.Application.Catalog.Commands.CreateProduct;
using TradeFlow.Application.Catalog.Commands.UpdateProduct;
using TradeFlow.Application.Catalog.Queries;
using TradeFlow.Application.Catalog.Queries.GetProductById;
using TradeFlow.Application.Catalog.Queries.GetProducts;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog/products")
            .WithTags("Catalog");

        group.MapGet("/", GetProductsAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderReadPolicy)
            .WithName("GetCatalogProducts")
            .WithSummary("Lists commercial B2B products from the catalog with optional search, category, currency, and pagination.")
            .Produces<PagedResponse<ProductResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetProductByIdAsync)
            .RequireAuthorization(TradeFlowPolicies.OrderReadPolicy)
            .WithName("GetCatalogProductById")
            .WithSummary("Retrieves a catalog product by ID (served via Redis distributed cache when available).")
            .Produces<ProductResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateProductAsync)
            .RequireAuthorization(TradeFlowPolicies.AdminPolicy)
            .WithName("CreateCatalogProduct")
            .WithSummary("Creates a new commercial product in the Catalog bounded context (Admin only).")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", UpdateProductAsync)
            .RequireAuthorization(TradeFlowPolicies.AdminPolicy)
            .WithName("UpdateCatalogProduct")
            .WithSummary("Updates commercial metadata, pricing, or active status for an existing catalog product (Admin only).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetProductsAsync(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? currency,
        [FromQuery] bool? activeOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var query = new GetProductsQuery(
            search,
            category,
            currency,
            activeOnly ?? true,
            page ?? 1,
            pageSize ?? 20);

        var result = await sender.Send(query, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetProductByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProductByIdQuery(id), cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateProductAsync(
        CreateProductRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(
            request.Sku,
            request.Name,
            request.Category,
            request.TechnicalDescription,
            request.UnitPrice,
            request.Currency,
            request.UnitOfMeasure ?? "Metric Ton",
            request.MinimumOrderQuantity ?? 1);

        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(id => $"/api/catalog/products/{id}");
    }

    private static async Task<IResult> UpdateProductAsync(
        Guid id,
        UpdateProductRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new UpdateProductCommand(
            id,
            request.Name,
            request.Category,
            request.TechnicalDescription,
            request.UnitPrice,
            request.Currency,
            request.UnitOfMeasure,
            request.MinimumOrderQuantity,
            request.IsActive);

        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult();
    }
}

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string Category,
    string TechnicalDescription,
    decimal UnitPrice,
    string Currency,
    string? UnitOfMeasure = "Metric Ton",
    int? MinimumOrderQuantity = 1);

public sealed record UpdateProductRequest(
    string Name,
    string Category,
    string TechnicalDescription,
    decimal UnitPrice,
    string Currency,
    string UnitOfMeasure,
    int MinimumOrderQuantity,
    bool IsActive);
