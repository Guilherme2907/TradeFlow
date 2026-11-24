using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Catalog.Exceptions;

public sealed class CatalogInvariantException : DomainException
{
    private CatalogInvariantException(string code, string message) : base(code, message)
    {
    }

    public static CatalogInvariantException InvalidSku()
        => new("CATALOG_INVALID_SKU", "Product SKU must not be empty or exceed 100 characters.");

    public static CatalogInvariantException InvalidName()
        => new("CATALOG_INVALID_NAME", "Product name must not be empty or exceed 200 characters.");

    public static CatalogInvariantException InvalidCategory()
        => new("CATALOG_INVALID_CATEGORY", "Product category must not be empty or exceed 100 characters.");

    public static CatalogInvariantException InvalidDescription()
        => new("CATALOG_INVALID_DESCRIPTION", "Technical description must not be empty or exceed 2000 characters.");

    public static CatalogInvariantException InvalidUnitPrice(decimal unitPrice)
        => new("CATALOG_INVALID_PRICE", $"Product unit price must be greater than zero. Provided: {unitPrice}.");

    public static CatalogInvariantException InvalidCurrency(string currency)
        => new("CATALOG_INVALID_CURRENCY", $"Currency must be a valid 3-letter ISO code. Provided: '{currency}'.");

    public static CatalogInvariantException InvalidUnitOfMeasure()
        => new("CATALOG_INVALID_UOM", "Unit of measure must not be empty or exceed 50 characters.");

    public static CatalogInvariantException InvalidMinimumOrderQuantity(int moq)
        => new("CATALOG_INVALID_MOQ", $"Minimum order quantity must be at least 1. Provided: {moq}.");
}
