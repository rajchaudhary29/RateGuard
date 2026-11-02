# Multi-stage Dockerfile for RateGuard Web API (Render & Cloud Production Ready)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy solution and project files for caching restore layer
COPY DistributedRateLimiter.sln ./
COPY src/DistributedRateLimiter.Api/DistributedRateLimiter.Api.csproj src/DistributedRateLimiter.Api/
COPY tests/DistributedRateLimiter.Tests/DistributedRateLimiter.Tests.csproj tests/DistributedRateLimiter.Tests/

RUN dotnet restore DistributedRateLimiter.sln

# Copy application source code
COPY Directory.Build.props ./
COPY src/ src/
COPY tests/ tests/

# Publish Release build
WORKDIR /source/src/DistributedRateLimiter.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# Lightweight runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root user for security compliance
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "DistributedRateLimiter.Api.dll"]
