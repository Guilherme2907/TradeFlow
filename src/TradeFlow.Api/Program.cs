using TradeFlow.Api.Endpoints;
using TradeFlow.Application;
using TradeFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Cross-cutting frameworks
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

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
}

app.UseHttpsRedirection();

// Map feature-oriented endpoint groups
app.MapOrderEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in API integration tests