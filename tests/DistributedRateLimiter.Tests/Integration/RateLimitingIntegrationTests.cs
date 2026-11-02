using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DistributedRateLimiter.Api.Data;
using DistributedRateLimiter.Api.Models;
using DistributedRateLimiter.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace DistributedRateLimiter.Tests.Integration;

/// <summary>
/// In-memory sliding-window counter implementation for integration testing without external Redis.
/// </summary>
public class InMemorySlidingWindowRateLimiter : IRateLimiterService
{
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _requestLogs = new();

    public Task<RateLimitResult> CheckRateLimitAsync(string clientKey, RateLimitRule rule, CancellationToken cancellationToken = default)
    {
        var key = $"{clientKey}:{rule.RuleName}";
        var timestamps = _requestLogs.GetOrAdd(key, _ => new List<DateTimeOffset>());
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddSeconds(-rule.WindowSeconds);

        lock (timestamps)
        {
            // Evict outside window
            timestamps.RemoveAll(t => t < windowStart);

            if (timestamps.Count < rule.PermitLimit)
            {
                timestamps.Add(now);
                var remaining = rule.PermitLimit - timestamps.Count;
                var resetTime = now.AddSeconds(rule.WindowSeconds);
                return Task.FromResult(RateLimitResult.Permitted(clientKey, rule.RuleName, rule.PermitLimit, timestamps.Count, remaining, resetTime));
            }
            else
            {
                var oldest = timestamps[0];
                var retryAfter = (int)Math.Max(1, Math.Ceiling((oldest.AddSeconds(rule.WindowSeconds) - now).TotalSeconds));
                var resetTime = now.AddSeconds(retryAfter);
                return Task.FromResult(RateLimitResult.Throttled(clientKey, rule.RuleName, rule.PermitLimit, timestamps.Count, retryAfter, resetTime));
            }
        }
    }
}

public class RateLimitingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing Redis and DB configurations
                var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<RateLimiterDbContext>));
                if (dbDescriptor != null)
                {
                    services.Remove(dbDescriptor);
                }

                var redisDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (redisDescriptor != null)
                {
                    services.Remove(redisDescriptor);
                }

                var rateLimiterDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRateLimiterService));
                if (rateLimiterDescriptor != null)
                {
                    services.Remove(rateLimiterDescriptor);
                }

                // Register In-Memory DbContext
                services.AddDbContext<RateLimiterDbContext>(options =>
                {
                    options.UseInMemoryDatabase("IntegrationTestDb_" + Guid.NewGuid().ToString());
                });

                // Register In-Memory Sliding Window Service
                services.AddSingleton<IRateLimiterService, InMemorySlidingWindowRateLimiter>();
            });
        });
    }

    [Fact]
    public async Task GetWeatherForecast_WhenWithinLimit_ReturnsSuccessAndHeaders()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");

        // Act
        var response = await client.GetAsync("/api/weatherforecast");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-RateLimit-Limit"));
        Assert.True(response.Headers.Contains("X-RateLimit-Remaining"));
        Assert.True(response.Headers.Contains("X-RateLimit-Reset"));
    }

    [Fact]
    public async Task BurstRequests_WhenAnonymousLimitExceeded_Returns429TooManyRequests()
    {
        // Arrange
        var client = _factory.CreateClient();
        var clientIp = "198.51.100.42";
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);

        // Anonymous limit is 5 requests per 10 seconds
        HttpResponseMessage lastResponse = null!;

        // Act
        for (int i = 0; i < 7; i++)
        {
            lastResponse = await client.GetAsync("/api/products");
            if (i < 5)
            {
                Assert.Equal(HttpStatusCode.OK, lastResponse.StatusCode);
            }
        }

        // Assert
        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse.StatusCode);
        Assert.True(lastResponse.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", lastResponse.Content.Headers.ContentType?.MediaType);

        var problemDetails = await lastResponse.Content.ReadFromJsonAsync<ProblemDetails>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(problemDetails);
        Assert.Equal(429, problemDetails.Status);
        Assert.Equal("Too Many Requests", problemDetails.Title);
        Assert.Equal("/api/products", problemDetails.Instance);
    }

    [Fact]
    public async Task PremiumApiKey_AllowsHigherQuotaThanAnonymous()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "key-premium-999");

        // Act: Make 15 requests (exceeding anonymous quota of 5, but within premium quota of 100)
        for (int i = 0; i < 15; i++)
        {
            var response = await client.GetAsync("/api/products");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task HealthEndpoint_IsBypassedFromRateLimits()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.99");

        // Act: send 20 requests to health check
        for (int i = 0; i < 20; i++)
        {
            var response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
