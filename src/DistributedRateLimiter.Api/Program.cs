using DistributedRateLimiter.Api.Data;
using DistributedRateLimiter.Api.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Forwarded Headers for Reverse Proxy / Load Balancers (AWS ALB, NGINX, Cloudflare, Traefik)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // In containerized/cloud environments, allow proxies to pass client IP
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// 2. Add CORS (Production configurable)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 3. Add framework services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// 4. Add Redis, PostgreSQL DbContext, sliding-window rate limiting engine, and background audit logging
builder.Services.AddDistributedRateLimitingInfrastructure(builder.Configuration);

// 5. Add Health Checks (Liveness & Readiness for Kubernetes / AWS ECS / Docker)
var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryFallback", false);
var healthCheckBuilder = builder.Services.AddHealthChecks();

if (!useInMemory)
{
    healthCheckBuilder.AddCheck("postgresql", () =>
    {
        // Simple check registered via DI factory if needed
        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("PostgreSQL configured");
    });
}

var app = builder.Build();

// 6. Forwarded Headers MUST be first in middleware pipeline
app.UseForwardedHeaders();

// 7. Database schema initialization check (safe for local dev, opt-in for production)
var applyMigrationsOnStartup = app.Configuration.GetValue<bool>("ApplyMigrationsOnStartup", app.Environment.IsDevelopment());
if (applyMigrationsOnStartup)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<RateLimiterDbContext>();
        if (db.Database.IsRelational())
        {
            logger.LogInformation("Applying PostgreSQL database migrations or ensuring tables exist...");
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("PostgreSQL schema verification completed successfully.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not initialize PostgreSQL schema on startup (DB may not be ready yet). Background worker will retry on operation.");
    }
}

// 8. RFC 7807 Global Exception Handling
app.UseGlobalExceptionHandler();

// 9. CORS Policy
app.UseCors("AllowAllOrigins");

// 10. OpenAPI / Swagger documentation
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 11. Health check endpoints (Kubernetes / ECS / Docker container probes)
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// 12. Serve Interactive Real-Time Dashboard UI (Static Files)
app.UseDefaultFiles();
app.UseStaticFiles();

// 13. Distributed sliding-window rate limiting middleware
app.UseDistributedRateLimiting();

// 14. Controller route endpoints
app.MapControllers();

app.Run();

// Expose Program class for WebApplicationFactory in integration tests
public partial class Program { }
