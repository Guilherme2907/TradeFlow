using System.Diagnostics;
using System.Text;
using FluentAssertions;
using TradeFlow.Infrastructure.Diagnostics;
using Xunit;

namespace TradeFlow.Infrastructure.IntegrationTests.Diagnostics;

/// <summary>
/// ADR-0015: Validates W3C TraceContext propagation and OpenTelemetry span hierarchy
/// across asynchronous messaging and persistence boundaries.
/// </summary>
public sealed class DistributedTracingPropagationTests
{
    [Fact]
    public void StartProducerActivity_ShouldPopulateOpenTelemetrySemanticConventions()
    {
        // Arrange
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TradeFlowDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        const string operationName = "RabbitMq.Publish orders.OrderSubmittedEvent";
        const string messagingSystem = "rabbitmq";
        const string destination = "tradeflow.events";
        var messageId = Guid.NewGuid().ToString();

        // Act
        using var activity = TradeFlowDiagnostics.StartProducerActivity(
            operationName,
            messagingSystem,
            destination,
            messageId);

        // Assert
        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Producer);
        activity.GetTagItem("messaging.system").Should().Be("rabbitmq");
        activity.GetTagItem("messaging.destination.name").Should().Be("tradeflow.events");
        activity.GetTagItem("messaging.message.id").Should().Be(messageId);
    }

    [Fact]
    public void StartConsumerActivity_WhenGivenTraceParent_ShouldCreateChildSpanWithMatchingTraceIdAndParentSpanId()
    {
        // Arrange: valid W3C traceparent (version 00, 32-char traceId, 16-char spanId, flags 01)
        const string incomingTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        const string expectedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        const string expectedParentSpanId = "00f067aa0ba902b7";

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TradeFlowDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        // Act: Consumer wakes up and extracts the traceparent from broker message headers
        using var consumerActivity = TradeFlowDiagnostics.StartConsumerActivity(
            name: "OrderSubmittedInvoiceConsumer.Handle",
            traceParent: incomingTraceParent,
            traceState: null,
            messagingSystem: "rabbitmq",
            destination: "orders.invoice.generation",
            messageId: Guid.NewGuid().ToString());

        // Assert: Causality is preserved! Child span shares the traceId and points to remote span as parent
        consumerActivity.Should().NotBeNull();
        consumerActivity!.Kind.Should().Be(ActivityKind.Consumer);
        consumerActivity.TraceId.ToString().Should().Be(expectedTraceId, "Consumer must belong to the exact same distributed trace tree");
        consumerActivity.ParentSpanId.ToString().Should().Be(expectedParentSpanId, "Consumer's parent must be the remote operation");
        consumerActivity.SpanId.ToString().Should().NotBe(expectedParentSpanId, "Consumer must generate its own unique SpanId");
    }

    [Fact]
    public void ExtractAmqpHeader_WhenGivenByteArray_ShouldDecodeUtf8String()
    {
        // Arrange
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes(traceParent)
        };

        // Act
        var result = TradeFlowDiagnostics.ExtractAmqpHeader(headers, "traceparent");

        // Assert
        result.Should().Be(traceParent);
    }

    [Fact]
    public void ExtractAmqpHeader_WhenGivenString_ShouldReturnDirectString()
    {
        // Arrange
        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = traceParent
        };

        // Act
        var result = TradeFlowDiagnostics.ExtractAmqpHeader(headers, "traceparent");

        // Assert
        result.Should().Be(traceParent);
    }
}
