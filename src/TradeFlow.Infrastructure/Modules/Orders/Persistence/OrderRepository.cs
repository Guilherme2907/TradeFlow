using Microsoft.EntityFrameworkCore;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence;

public class OrderRepository : IOrderRepository
{
    private readonly OrdersDbContext _context;

    public OrderRepository(OrdersDbContext context)
    {
        _context = context;
    }

    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListAsync(
        CustomerId? customerId = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Order> query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Lines);

        if (customerId is not null)
        {
            query = query.Where(o => o.CustomerId == customerId);
        }

        return await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _context.Orders.AddAsync(order, cancellationToken);
    }

    public void Update(Order order)
    {
        _context.Orders.Update(order);
    }
}
