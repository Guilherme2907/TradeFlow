namespace TradeFlow.Application.Orders.Gateways;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
