using TradeFlow.Application.Common.Models;

namespace TradeFlow.Application.Common.Security;

public sealed record TokenExchangeResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    string? RefreshToken);

/// <summary>
/// Application contract for OAuth2 / OIDC credential exchange against the external Identity Provider (Keycloak).
/// Keeps HTTP transport and Polly resilience concerns encapsulated inside the Infrastructure layer.
/// </summary>
public interface IIdentityProviderService
{
    Task<Result<TokenExchangeResponse>> ExchangeCredentialsForTokenAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}
