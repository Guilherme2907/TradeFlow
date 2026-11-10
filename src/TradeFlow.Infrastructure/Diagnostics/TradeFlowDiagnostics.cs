using System.Diagnostics;
using System.Text;

namespace TradeFlow.Infrastructure.Diagnostics;

/// <summary>
/// Central diagnostic primitives and OpenTelemetry ActivitySource for TradeFlow (ADR-0015).
/// Enables end-to-end W3C TraceContext propagation across asynchronous boundaries (Outbox, RabbitMQ, Kafka).
/// </summary>
public static class TradeFlowDiagnostics
{
    public const string ActivitySourceName = "TradeFlow";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    /// <summary>
    /// Starts a Producer Activity when publishing a message to a messaging system (RabbitMQ / Kafka).
    /// Links the span to an optional originating parent trace (e.g. from transactional outbox metadata).
    /// </summary>
    public static Activity? StartProducerActivity(
        string name,
        string messagingSystem,
        string destination,
        string? messageId = null,
        string? traceParent = null,
        string? traceState = null)
    {
        ActivityContext parentContext = default;
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            ActivityContext.TryParse(traceParent, traceState, out parentContext);
        }

        var activity = ActivitySource.StartActivity(
            name,
            ActivityKind.Producer,
            parentContext);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", messagingSystem);
            activity.SetTag("messaging.destination.name", destination);
            if (messageId is not null)
            {
                activity.SetTag("messaging.message.id", messageId);
            }
        }
        return activity;
    }

    /// <summary>
    /// Records an exception event on the activity following OpenTelemetry semantic conventions
    /// and sets the activity status to <see cref="ActivityStatusCode.Error"/>.
    /// </summary>
    public static void RecordError(this Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        var tags = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName ?? exception.GetType().Name },
            { "exception.message", exception.Message },
            { "exception.stacktrace", exception.ToString() }
        };

        activity.AddEvent(new ActivityEvent("exception", default, tags));
    }

    /// <summary>
    /// Starts a Consumer Activity from extracted W3C traceparent/tracestate,
    /// establishing causality by linking the child Span to the originating parent trace.
    /// </summary>
    public static Activity? StartConsumerActivity(
        string name,
        string? traceParent,
        string? traceState,
        string messagingSystem,
        string destination,
        string? messageId = null)
    {
        ActivityContext parentContext = default;
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            ActivityContext.TryParse(traceParent, traceState, out parentContext);
        }

        var activity = ActivitySource.StartActivity(
            name,
            ActivityKind.Consumer,
            parentContext);

        if (activity is not null)
        {
            activity.SetTag("messaging.system", messagingSystem);
            activity.SetTag("messaging.destination.name", destination);
            if (messageId is not null)
            {
                activity.SetTag("messaging.message.id", messageId);
            }
        }

        return activity;
    }

    /// <summary>
    /// Helper to extract header strings from AMQP object dictionary headers.
    /// </summary>
    public static string? ExtractAmqpHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string str => str,
            _ => raw.ToString()
        };
    }
}
