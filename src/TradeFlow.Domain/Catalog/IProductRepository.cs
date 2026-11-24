using TradeFlow.Domain.Common;

namespace TradeFlow.Domain.Catalog;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);
    Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchAsync(
        string? search,
        string? category,
        string? currency,
        bool? activeOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    void Update(Product product);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
