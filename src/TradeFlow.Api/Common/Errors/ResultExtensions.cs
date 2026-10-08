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
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Not Found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code
        };

        return TypedResults.Problem(
            statusCode: statusCode,
            title: title,
            detail: error.Description,
            type: $"https://tradeflow.com/errors/{error.Code.ToLowerInvariant().Replace('_', '-')}",
            extensions: extensions);
    }
}
