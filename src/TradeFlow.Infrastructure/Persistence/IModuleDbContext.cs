namespace TradeFlow.Infrastructure.Persistence;

/// <summary>
/// Marker implemented by every bounded-context DbContext so the <see cref="UnitOfWork"/>
/// can coordinate a single shared transaction without knowing individual modules (ADR-0010).
/// </summary>
public interface IModuleDbContext
{
}
