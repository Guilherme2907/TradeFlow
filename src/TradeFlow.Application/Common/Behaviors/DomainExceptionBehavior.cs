using System.Reflection;
using MediatR;
using TradeFlow.Application.Common.Models;
using TradeFlow.Domain.Common;

namespace TradeFlow.Application.Common.Behaviors;

/// <summary>
/// Pipeline behavior that intercepts DomainException instances thrown by aggregate roots
/// or domain services and translates them cleanly into Result.Failure models.
/// Eliminates try-catch boilerplate in individual command handlers.
/// </summary>
public class DomainExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (DomainException ex)
        {
            var error = Error.Validation(ex.Code, ex.Message);

            // If TResponse is Result, return Result.Failure
            if (typeof(TResponse) == typeof(Result))
            {
                return (TResponse)(object)Result.Failure(error);
            }

            // If TResponse is Result<TValue>, create Result<TValue>.Failure(error)
            if (typeof(TResponse).IsGenericType &&
                typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
            {
                var failureMethod = typeof(Result)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition)
                    .MakeGenericMethod(typeof(TResponse).GetGenericArguments()[0]);

                return (TResponse)failureMethod.Invoke(null, [error])!;
            }

            throw;
        }
    }
}
