using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradeFlow.Infrastructure.Diagnostics;

namespace TradeFlow.Infrastructure.Messaging.Kafka;

/// <summary>
/// Long-running Kafka consumer worker that processes <see cref="KafkaTopology.TradesTopic"/> records in strict
/// partition order, commits offsets manually after processing, performs bounded in-memory transient retries,
/// and isolates unrecoverable poison-pill messages into <see cref="KafkaTopology.TradesDeadLetterTopic"/>
/// to prevent Head-of-Line Partition Blocking (ADR-0012).
/// </summary>
public sealed class KafkaTradeStreamConsumerBackgroundService : BackgroundService
{
    public const int MaxTransientAttempts = 3;

    private readonly string _bootstrapServers;
    private readonly string _consumerGroupId;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKafkaTradeStreamProducer _producer;
    private readonly ILogger<KafkaTradeStreamConsumerBackgroundService> _logger;

    public KafkaTradeStreamConsumerBackgroundService(
        string bootstrapServers,
        IServiceScopeFactory scopeFactory,
        IKafkaTradeStreamProducer producer,
        ILogger<KafkaTradeStreamConsumerBackgroundService> logger,
        string consumerGroupId = KafkaTopology.DefaultConsumerGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        _bootstrapServers = bootstrapServers;
        _scopeFactory = scopeFactory;
        _producer = producer;
        _logger = logger;
        _consumerGroupId = consumerGroupId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield immediately so the host startup continues without blocking
        await Task.Yield();

        try
        {
            await KafkaTopology.EnsureTopicsCreatedAsync(_bootstrapServers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify Kafka topics on startup at {BootstrapServers}. Consumer will retry during poll loop.", _bootstrapServers);
        }

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = _consumerGroupId,
            // ADR-0012: Disable automatic background offset commits; commit strictly after processing
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            // Start from Offset 0 when a new Consumer Group attaches (enables historical replay)
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SessionTimeoutMs = 10000,
            HeartbeatIntervalMs = 3000
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetErrorHandler((_, error) =>
            {
                if (error.IsFatal)
                {
                    _logger.LogError("Fatal Kafka consumer error: {Reason} (Code: {Code})", error.Reason, error.Code);
                }
                else
                {
                    _logger.LogDebug("Kafka consumer notice: {Reason}", error.Reason);
                }
            })
            .Build();

        consumer.Subscribe(KafkaTopology.TradesTopic);

        _logger.LogInformation(
            "Kafka consumer subscribed to '{Topic}' in Consumer Group '{GroupId}' (EnableAutoCommit = false).",
            KafkaTopology.TradesTopic,
            _consumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumeResult = null;
                try
                {
                    consumeResult = consumer.Consume(TimeSpan.FromMilliseconds(200));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Transient Kafka consume error: {Reason}", ex.Error.Reason);
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                if (consumeResult is null || consumeResult.IsPartitionEOF)
                {
                    continue;
                }

                await ProcessRecordWithPoisonPillIsolationAsync(consumer, consumeResult, stoppingToken);
            }
        }
        finally
        {
            try
            {
                consumer.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception while closing Kafka consumer.");
            }
        }
    }

    private async Task ProcessRecordWithPoisonPillIsolationAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> consumeResult,
        CancellationToken stoppingToken)
    {
        var partition = consumeResult.Partition.Value;
        var offset = consumeResult.Offset.Value;
        var key = consumeResult.Message.Key ?? string.Empty;
        var rawPayload = consumeResult.Message.Value ?? string.Empty;

        string? traceParent = null;
        string? traceState = null;
        if (consumeResult.Message.Headers is not null)
        {
            foreach (var header in consumeResult.Message.Headers)
            {
                if (header.Key.Equals("traceparent", StringComparison.OrdinalIgnoreCase))
                {
                    traceParent = Encoding.UTF8.GetString(header.GetValueBytes());
                }
                else if (header.Key.Equals("tracestate", StringComparison.OrdinalIgnoreCase))
                {
                    traceState = Encoding.UTF8.GetString(header.GetValueBytes());
                }
            }
        }

        using var activity = TradeFlowDiagnostics.StartConsumerActivity(
            name: "KafkaTradeStream.Consume",
            traceParent: traceParent,
            traceState: traceState,
            messagingSystem: "kafka",
            destination: consumeResult.Topic,
            messageId: $"{partition}:{offset}");

        try
        {
            var tradeEvent = MarketTradeStreamEvent.Deserialize(rawPayload);

            await ExecuteHandlerWithTransientRetryAsync(tradeEvent, partition, offset, stoppingToken);

            // Commit offset strictly after successful processing
            consumer.Commit(consumeResult);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ADR-0012: Poison Pill Isolation — route to Dead-Letter Topic and commit offset on main partition
            // so subsequent valid events in this partition are NEVER blocked (prevents Head-of-Line Blocking).
            _logger.LogError(
                ex,
                "[Kafka PoisonPill] Failed to process record at {Topic} [Partition: {Partition}, Offset: {Offset}, Key: {Key}]. Routing to Dead-Letter Topic '{Dlt}'.",
                consumeResult.Topic,
                partition,
                offset,
                key,
                KafkaTopology.TradesDeadLetterTopic);

            var dltHeaders = new Dictionary<string, string>
            {
                ["x-error-type"] = ex.GetType().Name,
                ["x-error-message"] = ex.Message,
                ["x-original-topic"] = consumeResult.Topic,
                ["x-original-partition"] = partition.ToString(),
                ["x-original-offset"] = offset.ToString()
            };

            await _producer.PublishRawAsync(
                KafkaTopology.TradesDeadLetterTopic,
                key,
                rawPayload,
                dltHeaders,
                stoppingToken);

            // Advance offset past the poison pill so the partition remains healthy and unblocked
            consumer.Commit(consumeResult);
        }
    }

    private async Task ExecuteHandlerWithTransientRetryAsync(
        MarketTradeStreamEvent tradeEvent,
        int partition,
        int offset,
        CancellationToken stoppingToken)
        => await ExecuteHandlerWithTransientRetryAsync(tradeEvent, partition, (long)offset, stoppingToken);

    private async Task ExecuteHandlerWithTransientRetryAsync(
        MarketTradeStreamEvent tradeEvent,
        int partition,
        long offset,
        CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxTransientAttempts; attempt++)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IMarketTradeStreamHandler>();

                await handler.HandleAsync(tradeEvent, partition, offset, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxTransientAttempts)
            {
                var delayMs = attempt * 50;
                _logger.LogWarning(
                    ex,
                    "[Kafka Retry] Transient failure processing trade {EventId} at [Partition: {Partition}, Offset: {Offset}] (Attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs}ms...",
                    tradeEvent.EventId,
                    partition,
                    offset,
                    attempt,
                    MaxTransientAttempts,
                    delayMs);

                await Task.Delay(delayMs, stoppingToken);
            }
        }
    }
}
