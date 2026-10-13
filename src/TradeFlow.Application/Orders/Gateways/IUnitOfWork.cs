using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Orders.Gateways;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single transaction spanning all module contexts (ADR-0010).
    /// <para>
    /// RETRY CONTRACT: after a transient failure the delegate is executed again. It must therefore load every
    /// aggregate it mutates inside the delegate; change trackers are cleared before each re-attempt.
    /// A failed <see cref="Result"/> rolls the transaction back.
    /// </para>
    /// </summary>
    Task<Result> ExecuteInTransactionAsync(Func<Task<Result>> operation, CancellationToken cancellationToken = default);
}
