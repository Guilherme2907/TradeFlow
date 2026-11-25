using Microsoft.OpenApi;
using Scalar.AspNetCore;
using TradeFlow.Api.Common;
using TradeFlow.Api.Common.Health;
using TradeFlow.Api.Common.Resilience;
using TradeFlow.Api.Common.Security;
using TradeFlow.Api.Endpoints;
using TradeFlow.Application;
using TradeFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting frameworks & Security (OAuth2 / OIDC JWT Bearer + RBAC Policies + Strict CORS Allowlist)
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient();

var allowedCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("TradeFlowCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Retry-After", "Location", "X-Correlation-ID");
    });
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        var bearerScheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste your Keycloak JWT Access Token here (obtain one via POST /api/auth/token)."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = bearerScheme;

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });

        return Task.CompletedTask;
    });
});
builder.Services.AddTradeFlowSecurity(builder.Configuration);
builder.Services.AddTradeFlowTelemetry(builder.Configuration);
builder.Services.AddTradeFlowResilience(builder.Configuration);
builder.Services.AddTradeFlowHealthChecks(builder.Configuration);

// Clean Architecture layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure HTTP request pipeline
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("TradeFlow B2B Trading Platform API");
    });
    await app.MigrateAndSeedDevelopmentDatabaseAsync();
}

app.UseHttpsRedirection();

app.UseCors("TradeFlowCorsPolicy");

app.UseAuthentication();
app.UseTradeFlowResilience();
app.UseAuthorization();

// Map Cloud-Native Kubernetes Health Probes & feature-oriented endpoint groups
app.MapTradeFlowHealthChecks();
app.MapAuthEndpoints();
app.MapOrderEndpoints();
app.MapInventoryEndpoints();
app.MapCatalogEndpoints();
app.MapCustomerEndpoints();
app.MapAiEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in API integration tests
public partial class Program { }
