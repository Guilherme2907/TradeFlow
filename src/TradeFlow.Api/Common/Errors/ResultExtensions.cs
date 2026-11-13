using Microsoft.AspNetCore.Http;
using TradeFlow.Application.Common.Models;

namespace TradeFlow.Api.Common.Errors;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result, string? successLocation = null)
    {
        if (result.IsSuccess)
        {
            return successLocation is not null
                ? TypedResults.Created(successLocation)
                : TypedResults.Ok();
        }

        return MapErrorToProblemDetails(result.Error);
    }

    public static IResult ToHttpResult<TValue>(
        this Result<TValue> result,
        Func<TValue, string>? locationFactory = null)
    {
        if (result.IsSuccess)
        {
            if (locationFactory is not null)
            {
                var location = locationFactory(result.Value);
                return TypedResults.Created(location, result.Value);
            }

            return TypedResults.Ok(result.Value);
        }

        return MapErrorToProblemDetails(result.Error);
    }

    private static IResult MapErrorToProblemDetails(Error error)
    {
        var (statusCode, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Bad Request"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not Found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code
        };

        var problemResult = TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            detail: error.Description,
            type: $"https://tradeflow.com/errors/{error.Code.ToLowerInvariant().Replace('_', '-')}",
            extensions: extensions);

        return error.Type == ErrorType.Unavailable
            ? new ProblemWithRetryAfterResult(problemResult, retryAfterSeconds: 15)
            : problemResult;
    }

    private sealed class ProblemWithRetryAfterResult(IResult innerResult, int retryAfterSeconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return innerResult.ExecuteAsync(httpContext);
        }
    }
}
