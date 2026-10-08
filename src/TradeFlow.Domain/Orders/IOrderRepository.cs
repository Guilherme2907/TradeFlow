using TradeFlow.Domain.Orders;

namespace TradeFlow.Domain.Orders;

/// <summary>
/// Domain repository interface governing persistence and retrieval
/// of the Order aggregate root.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    void Update(Order order);
}
