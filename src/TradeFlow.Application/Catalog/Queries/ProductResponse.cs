using TradeFlow.Domain.Catalog;

namespace TradeFlow.Application.Catalog.Queries;

public sealed record ProductResponse(
    Guid ProductId,
    string Sku,
    string Name,
    string Category,
    string TechnicalDescription,
    decimal UnitPrice,
    string Currency,
    string UnitOfMeasure,
    int MinimumOrderQuantity,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

internal static class ProductMappings
{
    public static ProductResponse ToResponse(this Product product) => new(
        product.Id.Value,
        product.Sku,
        product.Name,
        product.Category,
        product.TechnicalDescription,
        product.UnitPrice,
        product.Currency,
        product.UnitOfMeasure,
        product.MinimumOrderQuantity,
        product.IsActive,
        product.CreatedAt,
        product.UpdatedAt);
}
