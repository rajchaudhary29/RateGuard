using DistributedRateLimiter.Api.Data;
using DistributedRateLimiter.Api.Options;
using DistributedRateLimiter.Api.Services;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace DistributedRateLimiter.Api.Extensions;

/// <summary>
/// Extension methods for configuring distributed rate limiter services into the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Redis or In-Memory caching, PostgreSQL or In-Memory DbContext,
    /// sliding-window rate limiting engine, and background audit logging.
    /// </summary>
    public static IServiceCollection AddDistributedRateLimitingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 1. Configure Options
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));

        var useInMemory = configuration.GetValue<bool>("UseInMemoryFallback", false);

        if (useInMemory)
        {
            // --- LOCAL DEV IN-MEMORY MODE (Zero dependencies required) ---
            services.AddDbContext<RateLimiterDbContext>(options =>
            {
                options.UseInMemoryDatabase("DistributedRateLimiterDevDb");
            });

            services.AddSingleton<IRateLimiterService, InMemoryRateLimiterService>();
        }
        else
        {
            // --- PRODUCTION / DISTRIBUTED MODE (Redis + PostgreSQL) ---
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
                var config = ConfigurationOptions.Parse(redisOptions.ConnectionString);
                config.ConnectTimeout = redisOptions.ConnectTimeoutMs;
                config.SyncTimeout = redisOptions.SyncTimeoutMs;
                config.AbortOnConnectFail = redisOptions.AbortOnConnectFail;
                config.ConnectRetry = 3;

                return ConnectionMultiplexer.Connect(config);
            });

            var postgresConnection = configuration.GetConnectionString("PostgreSql")
                ?? "Host=localhost;Port=5432;Database=ratelimitdb;Username=postgres;Password=postgres;";

            services.AddDbContext<RateLimiterDbContext>(options =>
            {
                options.UseNpgsql(postgresConnection, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
            });

            services.AddSingleton<IRateLimiterService, RedisRateLimiterService>();
        }

        // Common Client Resolution & Background Audit Queue
        services.AddSingleton<IClientIdentifierResolver, ClientIdentifierResolver>();
        services.AddSingleton<AuditLogService>();
        services.AddSingleton<IAuditLogService>(sp => sp.GetRequiredService<AuditLogService>());
        services.AddHostedService<AuditLogBackgroundWorker>();

        return services;
    }
}
