using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TradeFlow.Infrastructure.Diagnostics;

namespace TradeFlow.Api.Common;

/// <summary>
/// Configures OpenTelemetry distributed tracing, metrics, and logs with W3C TraceContext propagation (ADR-0015).
/// Exports to OTLP (Jaeger/local) and/or Azure Application Insights depending on environment configuration.
/// </summary>
public static class TelemetryConfiguration
{
    public static IServiceCollection AddTradeFlowTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? configuration.GetConnectionString("Otlp");

        var appInsightsConnectionString = configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            ?? configuration["ApplicationInsights:ConnectionString"];

        var hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        if (string.IsNullOrWhiteSpace(otlpEndpoint) && !hasAppInsights)
        {
            otlpEndpoint = "http://localhost:4317";
        }

        var enableConsole = configuration.GetValue<bool>("OpenTelemetry:EnableConsoleExporter");

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "TradeFlow.Api", serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(TradeFlowDiagnostics.ActivitySourceName)
                    .AddSource("Polly")
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

                if (hasAppInsights)
                {
                    tracing.AddAzureMonitorTraceExporter(options =>
                    {
                        options.ConnectionString = appInsightsConnectionString;
                    });
                }
            });

        if (hasAppInsights)
        {
            otelBuilder
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAzureMonitorMetricExporter(options =>
                    {
                        options.ConnectionString = appInsightsConnectionString;
                    }))
                .WithLogging(logging => logging
                    .AddAzureMonitorLogExporter(options =>
                    {
                        options.ConnectionString = appInsightsConnectionString;
                    }));
        }

        return services;
    }
}
