using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TradeFlow.Application.Common.Security;

namespace TradeFlow.Api.Common.Security;

public static class TradeFlowPolicies
{
    public const string OrderWritePolicy = "OrderWritePolicy";
    public const string OrderCancelPolicy = "OrderCancelPolicy";
    public const string OrderReadPolicy = "OrderReadPolicy";
}

public static class SecurityExtensions
{
    public static IServiceCollection AddTradeFlowSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();

        var authority = configuration["Authentication:Authority"] ?? "http://localhost:8080/realms/tradeflow";
        var audience = configuration["Authentication:Audience"] ?? "tradeflow-api";
        var requireHttpsMetadata = !bool.TryParse(configuration["Authentication:RequireHttpsMetadata"], out var requireHttps) || requireHttps;

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = requireHttpsMetadata;
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudiences = [audience, "account"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            NormalizeRoleClaims(identity);
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(TradeFlowPolicies.OrderWritePolicy, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(TradeFlowRoles.Trader, TradeFlowRoles.Admin))
            .AddPolicy(TradeFlowPolicies.OrderCancelPolicy, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(TradeFlowRoles.Trader, TradeFlowRoles.RiskManager, TradeFlowRoles.Admin))
            .AddPolicy(TradeFlowPolicies.OrderReadPolicy, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(
                          TradeFlowRoles.Trader,
                          TradeFlowRoles.RiskManager,
                          TradeFlowRoles.ComplianceAuditor,
                          TradeFlowRoles.Admin));

        return services;
    }

    /// <summary>
    /// Normalizes both Keycloak's nested <c>realm_access.roles</c> JSON structure and flat <c>roles</c>/<c>role</c>
    /// claims into standard <see cref="ClaimTypes.Role"/> claims so ASP.NET Core Policy-Based Authorization works seamlessly.
    /// </summary>
    public static void NormalizeRoleClaims(ClaimsIdentity identity)
    {
        var extractedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in identity.FindAll("roles").Concat(identity.FindAll("role")).ToList())
        {
            if (!string.IsNullOrWhiteSpace(claim.Value))
            {
                extractedRoles.Add(claim.Value);
            }
        }

        var realmAccessClaim = identity.FindFirst(CurrentUserContext.KeycloakRealmAccessClaimType);
        if (realmAccessClaim is not null && !string.IsNullOrWhiteSpace(realmAccessClaim.Value))
        {
            CurrentUserContext.ExtractKeycloakRealmRoles(realmAccessClaim.Value, extractedRoles);
        }

        foreach (var role in extractedRoles)
        {
            if (!identity.HasClaim(ClaimTypes.Role, role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
    }
}
