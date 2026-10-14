using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TradeFlow.Domain.Common;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;

/// <summary>
/// Intercepts EF Core SaveChanges operations to atomically capture uncommitted domain events
/// from Aggregate Roots implementing <see cref="IHasDomainEvents"/> and persist them
/// as <see cref="OutboxMessage"/> entries within the exact same database transaction (ADR-0011).
/// </summary>
public sealed class DomainEventsToOutboxMessagesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            ConvertDomainEventsToOutboxMessages(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            ConvertDomainEventsToOutboxMessages(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private static void ConvertDomainEventsToOutboxMessages(DbContext context)
    {
        var entitiesWithEvents = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        if (entitiesWithEvents.Count == 0)
        {
            return;
        }

        var outboxMessages = new List<OutboxMessage>();

        foreach (var entity in entitiesWithEvents)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions);
                var outboxMessage = new OutboxMessage(
                    domainEvent.EventId,
                    domainEvent.GetType().Name,
                    payload,
                    domainEvent.OccurredOn);

                outboxMessages.Add(outboxMessage);
            }

            entity.ClearDomainEvents();
        }

        context.Set<OutboxMessage>().AddRange(outboxMessages);
    }
}
