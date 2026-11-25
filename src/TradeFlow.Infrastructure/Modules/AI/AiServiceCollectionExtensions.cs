using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;
using TradeFlow.Infrastructure.Modules.AI.Configuration;
using TradeFlow.Infrastructure.Modules.AI.Providers;

namespace TradeFlow.Infrastructure.Modules.AI;

public static class AiServiceCollectionExtensions
{
    public const string AiHttpClientName = "TradeFlowAiProviderHttpClient";
    public const string AiTelemetrySourceName = "TradeFlow.AI";

    public static IServiceCollection AddAiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var aiOptions = new AiOptions();
        configuration.GetSection(AiOptions.SectionName).Bind(aiOptions);
        services.AddSingleton(aiOptions);

        var resilience = aiOptions.Resilience;

        services.AddHttpClient(AiHttpClientName, client =>
            {
                // Delegate timeout enforcement strictly to the Polly v8 resilience pipeline
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddResilienceHandler("AiLlmResiliencePipeline", builder =>
            {
                // 1. Total outer deadline across retries
                builder.AddTimeout(TimeSpan.FromSeconds(resilience.TotalTimeoutSeconds));

                // 2. Selective Retry: retry ONLY transient HTTP 429 / 5xx / network connection failures.
                // Intentionally DO NOT retry TimeoutRejectedException so slow LLM generations never multiply token billing!
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = resilience.MaxRetries,
                    Delay = TimeSpan.FromMilliseconds(resilience.RetryBaseDelayMs),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = static args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException ||
                        args.Outcome.Result?.StatusCode is
                            HttpStatusCode.TooManyRequests or
                            HttpStatusCode.RequestTimeout or
                            HttpStatusCode.BadGateway or
                            HttpStatusCode.ServiceUnavailable or
                            HttpStatusCode.GatewayTimeout)
                });

                // 3. Circuit Breaker: fail fast when upstream LLM provider experiences sustained outage
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = resilience.CircuitBreakerFailureRatio,
                    MinimumThroughput = resilience.CircuitBreakerMinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(resilience.CircuitBreakerSamplingSeconds),
                    BreakDuration = TimeSpan.FromSeconds(resilience.CircuitBreakerBreakSeconds),
                    ShouldHandle = static args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or TimeoutRejectedException ||
                        args.Outcome.Result?.StatusCode is
                            HttpStatusCode.TooManyRequests or
                            HttpStatusCode.BadGateway or
                            HttpStatusCode.ServiceUnavailable or
                            HttpStatusCode.GatewayTimeout)
                });

                // 4. Per-attempt timeout realistic for structured LLM token generation (default 25s)
                builder.AddTimeout(TimeSpan.FromSeconds(resilience.AttemptTimeoutSeconds));
            });

        services.TryAddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<AiOptions>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var httpClient = httpClientFactory.CreateClient(AiHttpClientName);

            IChatClient innerClient = string.Equals(options.Provider, "Ollama", StringComparison.OrdinalIgnoreCase)
                ? new OllamaChatClient(httpClient, options.Ollama)
                : new GeminiChatClient(httpClient, options.Gemini);

            return new ChatClientBuilder(innerClient)
                .UseLogging(loggerFactory)
                .UseOpenTelemetry(loggerFactory, sourceName: AiTelemetrySourceName)
                .Build();
        });

        return services;
    }
}
