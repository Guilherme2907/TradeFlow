using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TradeFlow.Application.Common.Models;
using TradeFlow.Application.Common.Security;
using TradeFlow.Infrastructure.Resilience;

namespace TradeFlow.Infrastructure.Identity;

/// <summary>
/// Typed HTTP client adapter for Keycloak OAuth2/OIDC token exchange.
/// Protected by the "Keycloak" Polly v8 resilience pipeline and <see cref="ResilienceHttpExecutor"/>.
/// </summary>
public sealed class KeycloakIdentityProviderService(
    HttpClient httpClient,
    IConfiguration configuration) : IIdentityProviderService
{
    public async Task<Result<TokenExchangeResponse>> ExchangeCredentialsForTokenAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var authority = (configuration["Authentication:Authority"] ?? "http://localhost:8080/realms/tradeflow").TrimEnd('/');
        var clientId = configuration["Authentication:Audience"] ?? "tradeflow-api";
        var tokenEndpoint = $"{authority}/protocol/openid-connect/token";

        var sendResult = await httpClient.SendWithResilienceAsync(
            async (client, ct) =>
            {
                // FormUrlEncodedContent is recreated per call so Polly retries always have a fresh stream
                using var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = clientId,
                    ["username"] = username,
                    ["password"] = password
                });

                return await client.PostAsync(tokenEndpoint, formContent, ct);
            },
            dependencyName: "Keycloak Identity Provider",
            unavailableErrorCode: "IDENTITY_PROVIDER_UNAVAILABLE",
            cancellationToken);

        if (sendResult.IsFailure)
        {
            return Result.Failure<TokenExchangeResponse>(sendResult.Error);
        }

        using var response = sendResult.Value;

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<TokenExchangeResponse>(Error.Unauthorized(
                "AUTH_INVALID_CREDENTIALS",
                "Keycloak rejected the username or password."));
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var accessToken = payload.GetProperty("access_token").GetString() ?? string.Empty;
        var expiresIn = payload.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        var refreshToken = payload.TryGetProperty("refresh_token", out var refTok)
            ? refTok.GetString()
            : null;

        return Result.Success(new TokenExchangeResponse(
            AccessToken: accessToken,
            TokenType: "Bearer",
            ExpiresInSeconds: expiresIn,
            RefreshToken: refreshToken));
    }
}
