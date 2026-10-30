using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TradeFlow.Api.Common.Security;

namespace TradeFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Generates real RSA-2048 signed JWT Bearer tokens (<c>RS256</c>) replicating Keycloak's OIDC token structure
/// (<c>sub</c>, <c>customer_id</c>, and <c>realm_access.roles</c>) for end-to-end API security integration testing (ADR-0013).
/// </summary>
public static class TestJwtTokenGenerator
{
    public const string Issuer = "http://localhost:8080/realms/tradeflow";
    public const string Audience = "tradeflow-api";

    private static readonly RSA RsaInstance = RSA.Create(2048);

    public static RsaSecurityKey SigningKey { get; } = new(RsaInstance)
    {
        KeyId = "tradeflow-keycloak-test-rs256-key"
    };

    public static string GenerateToken(
        Guid userId,
        Guid? customerId,
        params string[] roles)
    {
        var claims = new Dictionary<string, object>
        {
            [CurrentUserContext.SubjectClaimType] = userId.ToString(),
            ["preferred_username"] = $"user-{userId:N}"
        };

        if (customerId.HasValue)
        {
            claims[CurrentUserContext.CustomerIdClaimType] = customerId.Value.ToString();
        }

        if (roles.Length > 0)
        {
            // Emit Keycloak's nested "realm_access": { "roles": [...] } JSON structure to verify OnTokenValidated normalization
            var realmAccessJson = JsonSerializer.SerializeToElement(new { roles });
            claims[CurrentUserContext.KeycloakRealmAccessClaimType] = realmAccessJson;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims,
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256)
        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(descriptor);
    }
}
