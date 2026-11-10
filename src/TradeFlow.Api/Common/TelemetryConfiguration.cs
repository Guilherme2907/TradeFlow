using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TradeFlow.Infrastructure.Diagnostics;

namespace TradeFlow.Api.Common;

/// <summary>
/// Configures OpenTelemetry distributed tracing and metrics with W3C TraceContext propagation (ADR-0015).
/// </summary>
public static class TelemetryConfiguration
{
    public static IServiceCollection AddTradeFlowTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? configuration.GetConnectionString("Otlp")
            ?? "http://localhost:4317";

        var enableConsole = configuration.GetValue<bool>("OpenTelemetry:EnableConsoleExporter");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "TradeFlow.Api", serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(TradeFlowDiagnostics.ActivitySourceName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            if (response.StatusCode >= 500)
                            {
                                var exceptionFeature = response.HttpContext.Features.Get<IExceptionHandlerPathFeature>()
                                    ?? response.HttpContext.Features.Get<IExceptionHandlerFeature>();

                                if (exceptionFeature?.Error is not null)
                                {
                                    activity.RecordError(exceptionFeature.Error);
                                }
                            }
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddNpgsql()
                    .AddRedisInstrumentation();

                if (enableConsole)
                {
                    tracing.AddConsoleExporter();
                }

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            });

        return services;
    }
}
