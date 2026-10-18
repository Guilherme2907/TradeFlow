using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Outbox;

/// <summary>
/// Background worker that continuously polls <c>orders.outbox_messages</c> via <see cref="OutboxProcessor"/>
/// using a non-overlapping <see cref="PeriodicTimer"/> (ADR-0011).
/// </summary>
public sealed class OutboxPublisherBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherBackgroundService> _logger;

    public OutboxPublisherBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPublisherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();

                var processedCount = await processor.ProcessBatchAsync(
                    OutboxProcessor.DefaultBatchSize,
                    stoppingToken);

                // Adaptive drain: if the batch was completely full, immediately poll the next batch
                // without waiting for the timer tick.
                if (processedCount == OutboxProcessor.DefaultBatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception occurred during outbox polling cycle.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
