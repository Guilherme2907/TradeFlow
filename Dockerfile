# ==============================================================================
# STAGE 1: Build & Publish (Heavy SDK image used strictly during build time)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# 1. Copy only project metadata first to maximize Docker layer caching.
#    If only .cs files change, Docker skips "dotnet restore" completely!
COPY ["src/TradeFlow.Domain/TradeFlow.Domain.csproj", "src/TradeFlow.Domain/"]
COPY ["src/TradeFlow.Application/TradeFlow.Application.csproj", "src/TradeFlow.Application/"]
COPY ["src/TradeFlow.Infrastructure/TradeFlow.Infrastructure.csproj", "src/TradeFlow.Infrastructure/"]
COPY ["src/TradeFlow.Api/TradeFlow.Api.csproj", "src/TradeFlow.Api/"]

RUN dotnet restore "src/TradeFlow.Api/TradeFlow.Api.csproj"

# 2. Copy the remaining source code and publish optimized Release binaries
COPY src/ src/
WORKDIR "/src/src/TradeFlow.Api"
RUN dotnet publish "TradeFlow.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ==============================================================================
# STAGE 2: Production Runtime (Minimal ASP.NET Core Alpine image, no SDK/compilers)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Install icu-libs for globalization support in Alpine Linux
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production

# Security Best Practice: Run container as non-root user ($APP_UID = 1654 in .NET 8/9/10 images)
USER $APP_UID
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TradeFlow.Api.dll"]
