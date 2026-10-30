using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

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
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> ExchangeTokenWithKeycloakAsync(
        TokenLoginRequest request,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var authority = (configuration["Authentication:Authority"] ?? "http://localhost:8080/realms/tradeflow").TrimEnd('/');
        var clientId = configuration["Authentication:Audience"] ?? "tradeflow-api";
        var tokenEndpoint = $"{authority}/protocol/openid-connect/token";

        using var httpClient = httpClientFactory.CreateClient();
        using var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = request.Username,
            ["password"] = request.Password
        });

        var response = await httpClient.PostAsync(tokenEndpoint, formContent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid Credentials",
                detail: "Keycloak rejected the username or password.");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var accessToken = payload.GetProperty("access_token").GetString() ?? string.Empty;
        var expiresIn = payload.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        var refreshToken = payload.TryGetProperty("refresh_token", out var refTok) ? refTok.GetString() : null;

        return TypedResults.Ok(new TokenExchangeResponse(
            AccessToken: accessToken,
            TokenType: "Bearer",
            ExpiresInSeconds: expiresIn,
            RefreshToken: refreshToken));
    }
}

public sealed record TokenLoginRequest(
    string Username = "trader.alpha",
    string Password = "trader123");

public sealed record TokenExchangeResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    string? RefreshToken);
