using System.Diagnostics;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using TradeFlow.Application.Common.Models;
using TradeFlow.Infrastructure.Diagnostics;

namespace TradeFlow.Infrastructure.Resilience;

/// <summary>
/// Centralizes execution of outbound HTTP calls protected by Polly v8 resilience pipelines.
/// Translates pipeline exceptions (<see cref="BrokenCircuitException"/>, <see cref="TimeoutRejectedException"/>,
/// <see cref="RateLimiterRejectedException"/>, <see cref="HttpRequestException"/>) and upstream 5xx responses
/// into <see cref="Result{TValue}"/> with <see cref="ErrorType.Unavailable"/> so individual HTTP clients
/// and API endpoints never duplicate try/catch blocks.
/// </summary>
public static class ResilienceHttpExecutor
{
    public static async Task<Result<HttpResponseMessage>> SendWithResilienceAsync(
        this HttpClient httpClient,
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> sendAction,
        string dependencyName,
        string unavailableErrorCode,
        CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await sendAction(httpClient, cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            Activity.Current.RecordError(ex);
            return Result.Failure<HttpResponseMessage>(Error.Unavailable(
                unavailableErrorCode,
                $"The external dependency ({dependencyName}) is temporarily unavailable. Circuit breaker is OPEN (Fail-Fast) to protect system stability."));
        }
        catch (TimeoutRejectedException ex)
        {
            Activity.Current.RecordError(ex);
            return Result.Failure<HttpResponseMessage>(Error.Unavailable(
                unavailableErrorCode,
                $"The external dependency ({dependencyName}) did not respond within the allowed resilience timeout."));
        }
        catch (RateLimiterRejectedException ex)
        {
            Activity.Current.RecordError(ex);
            return Result.Failure<HttpResponseMessage>(Error.Unavailable(
                unavailableErrorCode,
                $"Maximum concurrent requests to {dependencyName} reached (Bulkhead Isolation). Please retry shortly."));
        }
        catch (HttpRequestException ex)
        {
            Activity.Current.RecordError(ex);
            return Result.Failure<HttpResponseMessage>(Error.Unavailable(
                unavailableErrorCode,
                $"Could not establish a connection to the external dependency ({dependencyName})."));
        }

        if ((int)response.StatusCode >= 500)
        {
            var statusCode = (int)response.StatusCode;
            Activity.Current?.SetStatus(ActivityStatusCode.Error, $"{dependencyName} returned HTTP {statusCode}");
            response.Dispose();

            return Result.Failure<HttpResponseMessage>(Error.Unavailable(
                unavailableErrorCode,
                $"{dependencyName} returned an upstream server error (HTTP {statusCode}). Please retry shortly."));
        }

        return Result.Success(response);
    }
}
