using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Orders.Gateways;

namespace TradeFlow.Infrastructure.Persistence;

/// <summary>
/// Module-agnostic unit of work coordinating persistence across all registered bounded-context
/// DbContexts within the single-database modular monolith transaction boundary
/// (ADR-0002, ADR-0007, ADR-0010).
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly IReadOnlyList<DbContext> _contexts;

    public UnitOfWork(IEnumerable<IModuleDbContext> moduleContexts)
    {
        _contexts = moduleContexts.Cast<DbContext>().ToList();
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var saved = 0;
        foreach (var context in _contexts)
        {
            saved += await context.SaveChangesAsync(cancellationToken);
        }

        return saved;
    }

    public async Task<Result> ExecuteInTransactionAsync(
        Func<Task<Result>> operation,
        CancellationToken cancellationToken = default)
    {
        // Fallback for non-relational test providers (e.g., InMemory provider)
        if (_contexts.Any(c => !c.Database.IsRelational()))
        {
            return await operation();
        }

        var attempt = 0;

        async Task<Result> RunAttemptAsync()
        {
            // A retried attempt must start from the database state, not from mutated in-memory entities.
            if (attempt++ > 0)
            {
                foreach (var context in _contexts)
                {
                    context.ChangeTracker.Clear();
                }
            }

            var primary = _contexts[0];
            await using var transaction = await primary.Database.BeginTransactionAsync(cancellationToken);
            var dbTransaction = transaction.GetDbTransaction();

            // All module contexts share one physical connection (ADR-0010): enlist them in the same transaction.
            foreach (var context in _contexts.Skip(1))
            {
                await context.Database.UseTransactionAsync(dbTransaction, cancellationToken);
            }

            try
            {
                var result = await operation();
                if (result.IsFailure)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return result;
                }

                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
            finally
            {
                // Detach enlisted contexts from the finished transaction.
                foreach (var context in _contexts.Skip(1))
                {
                    await context.Database.UseTransactionAsync(null, cancellationToken);
                }
            }
        }

        // Each context contributes its own retrying execution strategy; fold them into one delegate chain.
        Func<Task<Result>> pipeline = RunAttemptAsync;
        foreach (var context in _contexts.Reverse())
        {
            var inner = pipeline;
            var strategy = context.Database.CreateExecutionStrategy();
            pipeline = () => strategy.ExecuteAsync(inner);
        }

        return await pipeline();
    }
}
