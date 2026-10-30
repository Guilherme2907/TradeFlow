using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TradeFlow.Application.Common.Security;

namespace TradeFlow.Api.Common.Security;

/// <summary>
/// Presentation-layer adapter that extracts the authenticated caller's identity, roles, and
/// cryptographically signed <c>customer_id</c> tenant claim from <see cref="IHttpContextAccessor"/> (ADR-0013).
/// </summary>
public sealed class CurrentUserContext : ICurrentUserContext
{
    public const string CustomerIdClaimType = "customer_id";
    public const string SubjectClaimType = "sub";
    public const string KeycloakRealmAccessClaimType = "realm_access";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid UserId
    {
        get
        {
            var rawUserId = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? Principal?.FindFirstValue(SubjectClaimType);

            return Guid.TryParse(rawUserId, out var userId) ? userId : Guid.Empty;
        }
    }

    public Guid? CustomerId
    {
        get
        {
            var rawCustomerId = Principal?.FindFirstValue(CustomerIdClaimType);
            return Guid.TryParse(rawCustomerId, out var customerId) ? customerId : null;
        }
    }

    public IReadOnlySet<string> Roles
    {
        get
        {
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Principal is null)
            {
                return roles;
            }

            foreach (var claim in Principal.FindAll(ClaimTypes.Role))
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    roles.Add(claim.Value);
                }
            }

            foreach (var claim in Principal.FindAll("roles"))
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    roles.Add(claim.Value);
                }
            }

            // Also support Keycloak's nested "realm_access": { "roles": ["Trader", ...] } JSON claim
            var realmAccessJson = Principal.FindFirstValue(KeycloakRealmAccessClaimType);
            if (!string.IsNullOrWhiteSpace(realmAccessJson))
            {
                ExtractKeycloakRealmRoles(realmAccessJson, roles);
            }

            return roles;
        }
    }

    public bool IsInRole(string role) => Roles.Contains(role);

    public static void ExtractKeycloakRealmRoles(string realmAccessJson, ISet<string> targetRoles)
    {
        try
        {
            using var document = JsonDocument.Parse(realmAccessJson);
            if (document.RootElement.TryGetProperty("roles", out var rolesArray) &&
                rolesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in rolesArray.EnumerateArray())
                {
                    var roleName = element.GetString();
                    if (!string.IsNullOrWhiteSpace(roleName))
                    {
                        targetRoles.Add(roleName);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed realm_access claim
        }
    }
}
