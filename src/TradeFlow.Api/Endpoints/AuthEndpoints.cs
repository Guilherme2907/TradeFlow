using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TradeFlow.Api.Common.Errors;
using TradeFlow.Application.Common.Security;

namespace TradeFlow.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication (OAuth2 / OIDC)");

        group.MapPost("/token", ExchangeTokenWithKeycloakAsync)
            .AllowAnonymous()
            .WithName("ObtainAccessToken")
            .WithSummary("Exchanges user credentials for a signed Keycloak OAuth2/OIDC JWT Access Token.")
            .Produces<TokenExchangeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ExchangeTokenWithKeycloakAsync(
        TokenLoginRequest request,
        IIdentityProviderService identityProvider,
        CancellationToken cancellationToken)
    {
        var result = await identityProvider.ExchangeCredentialsForTokenAsync(
            request.Username,
            request.Password,
            cancellationToken);

        return result.ToHttpResult();
    }
}

public sealed record TokenLoginRequest(
    string Username = "trader.alpha",
    string Password = "trader123");
