using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Orders.Gateways;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes <paramref name="operation"/> inside a single atomic transaction spanning all registered module contexts (ADR-0010).
    /// <para>
    /// FAIL-FAST CONTRACT: In critical transaction processing with pessimistic locks, transactions are strictly
    /// fail-fast (no transparent ORM-level retries). Any failure rolls back all participating contexts.
    /// Resilience and retries are managed at the edge/boundary using idempotency keys and fresh execution scopes.
    /// </para>
    /// </summary>
    Task<Result> ExecuteInTransactionAsync(Func<Task<Result>> operation, CancellationToken cancellationToken = default);
}
