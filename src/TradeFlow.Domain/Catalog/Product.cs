using TradeFlow.Domain.Catalog.Events;
using TradeFlow.Domain.Catalog.Exceptions;
using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Catalog;

/// <summary>
/// Aggregate root representing a commercial B2B commodity or industrial product in the Catalog bounded context.
/// Owns reference pricing, SKU metadata, and technical specifications (read-heavy), completely isolated from
/// high-contention Inventory stock locks and point-in-time OrderLine snapshots (ADR-0001 / ADR-0007).
/// </summary>
public class Product : AggregateRoot<ProductId>
{
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string TechnicalDescription { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public int MinimumOrderQuantity { get; private set; }
    public bool IsActive { get; private set; }

    // Required by EF Core for entity materialization
    private Product() { }

    private Product(
        ProductId id,
        string sku,
        string name,
        string category,
        string technicalDescription,
        decimal unitPrice,
        string currency,
        string unitOfMeasure,
        int minimumOrderQuantity,
        DateTimeOffset? createdAt = null) : base(id, createdAt)
    {
        Sku = ValidateSku(sku);
        Name = ValidateName(name);
        Category = ValidateCategory(category);
        TechnicalDescription = ValidateDescription(technicalDescription);
        UnitPrice = ValidateUnitPrice(unitPrice);
        Currency = ValidateCurrency(currency);
        UnitOfMeasure = ValidateUnitOfMeasure(unitOfMeasure);
        MinimumOrderQuantity = ValidateMoq(minimumOrderQuantity);
        IsActive = true;

        RaiseDomainEvent(new ProductCreatedDomainEvent(Id, Sku, Name, UnitPrice, Currency, CreatedAt));
    }

    public static Product Create(
        string sku,
        string name,
        string category,
        string technicalDescription,
        decimal unitPrice,
        string currency,
        string unitOfMeasure = "Metric Ton",
        int minimumOrderQuantity = 1,
        ProductId? explicitId = null,
        DateTimeOffset? createdAt = null)
    {
        var productId = explicitId is { Value: var v } && v != Guid.Empty
            ? explicitId.Value
            : ProductId.New();

        return new Product(
            productId,
            sku,
            name,
            category,
            technicalDescription,
            unitPrice,
            currency,
            unitOfMeasure,
            minimumOrderQuantity,
            createdAt);
    }

    public void UpdateDetails(
        string name,
        string category,
        string technicalDescription,
        string unitOfMeasure,
        int minimumOrderQuantity)
    {
        Name = ValidateName(name);
        Category = ValidateCategory(category);
        TechnicalDescription = ValidateDescription(technicalDescription);
        UnitOfMeasure = ValidateUnitOfMeasure(unitOfMeasure);
        MinimumOrderQuantity = ValidateMoq(minimumOrderQuantity);
        Touch();
    }

    public void UpdatePrice(decimal newUnitPrice, string currency)
    {
        var validatedPrice = ValidateUnitPrice(newUnitPrice);
        var validatedCurrency = ValidateCurrency(currency);

        if (UnitPrice == validatedPrice && Currency == validatedCurrency)
            return;

        var previousPrice = UnitPrice;
        UnitPrice = validatedPrice;
        Currency = validatedCurrency;
        Touch();

        RaiseDomainEvent(new ProductPriceChangedDomainEvent(
            Id,
            Sku,
            previousPrice,
            UnitPrice,
            Currency,
            DateTimeOffset.UtcNow));
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        Touch();
    }

    private static string ValidateSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku) || sku.Trim().Length > 100)
            throw CatalogInvariantException.InvalidSku();
        return sku.Trim().ToUpperInvariant();
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw CatalogInvariantException.InvalidName();
        return name.Trim();
    }

    private static string ValidateCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 100)
            throw CatalogInvariantException.InvalidCategory();
        return category.Trim();
    }

    private static string ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 2000)
            throw CatalogInvariantException.InvalidDescription();
        return description.Trim();
    }

    private static decimal ValidateUnitPrice(decimal unitPrice)
    {
        if (unitPrice <= 0m)
            throw CatalogInvariantException.InvalidUnitPrice(unitPrice);
        return Math.Round(unitPrice, 4, MidpointRounding.AwayFromZero);
    }

    private static string ValidateCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
            throw CatalogInvariantException.InvalidCurrency(currency ?? string.Empty);
        return currency.Trim().ToUpperInvariant();
    }

    private static string ValidateUnitOfMeasure(string uom)
    {
        if (string.IsNullOrWhiteSpace(uom) || uom.Trim().Length > 50)
            throw CatalogInvariantException.InvalidUnitOfMeasure();
        return uom.Trim();
    }

    private static int ValidateMoq(int moq)
    {
        if (moq < 1)
            throw CatalogInvariantException.InvalidMinimumOrderQuantity(moq);
        return moq;
    }
}
