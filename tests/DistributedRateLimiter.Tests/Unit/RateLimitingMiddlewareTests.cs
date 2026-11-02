using System.IO;
using System.Text.Json;
using DistributedRateLimiter.Api.Middleware;
using DistributedRateLimiter.Api.Models;
using DistributedRateLimiter.Api.Models.Entities;
using DistributedRateLimiter.Api.Options;
using DistributedRateLimiter.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DistributedRateLimiter.Tests.Unit;

public class RateLimitingMiddlewareTests
{
    private readonly Mock<ILogger<RateLimitingMiddleware>> _loggerMock = new();
    private readonly Mock<IClientIdentifierResolver> _resolverMock = new();
    private readonly Mock<IRateLimiterService> _rateLimiterMock = new();
    private readonly Mock<IAuditLogService> _auditLogMock = new();
    private readonly RateLimitOptions _options = new();

    private RateLimitingMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new RateLimitingMiddleware(next, Options.Create(_options), _loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestIsAllowed_CallsNextAndSetsRateLimitHeaders()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/products";
        context.Request.Method = "GET";

        var client = new ClientIdentity("apikey:test-client", "127.0.0.1", "Standard", "test-client");
        _resolverMock.Setup(r => r.ResolveClientIdentity(context)).Returns(client);

        var resetTime = DateTimeOffset.UtcNow.AddSeconds(60);
        var permittedResult = RateLimitResult.Permitted(client.ClientKey, "Default", 20, 1, 19, resetTime);
        _rateLimiterMock.Setup(r => r.CheckRateLimitAsync(client.ClientKey, It.IsAny<RateLimitRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permittedResult);

        // Act
        await middleware.InvokeAsync(context, _resolverMock.Object, _rateLimiterMock.Object, _auditLogMock.Object);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("20", context.Response.Headers["X-RateLimit-Limit"]);
        Assert.Equal("19", context.Response.Headers["X-RateLimit-Remaining"]);
        Assert.Equal(resetTime.ToUnixTimeSeconds().ToString(), context.Response.Headers["X-RateLimit-Reset"]);
        _auditLogMock.Verify(a => a.LogViolation(It.IsAny<RateLimitViolationLog>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_WhenLimitExceeded_ShortCircuitsWith429AndProblemDetails()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (_) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/products";
        context.Request.Method = "POST";

        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        var client = new ClientIdentity("ip:192.168.1.50", "192.168.1.50", "Anonymous", null);
        _resolverMock.Setup(r => r.ResolveClientIdentity(context)).Returns(client);

        var resetTime = DateTimeOffset.UtcNow.AddSeconds(15);
        var throttledResult = RateLimitResult.Throttled(client.ClientKey, "Default", 5, 6, 15, resetTime);
        _rateLimiterMock.Setup(r => r.CheckRateLimitAsync(client.ClientKey, It.IsAny<RateLimitRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(throttledResult);

        // Act
        await middleware.InvokeAsync(context, _resolverMock.Object, _rateLimiterMock.Object, _auditLogMock.Object);

        // Assert
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("15", context.Response.Headers["Retry-After"]);
        Assert.Equal("0", context.Response.Headers["X-RateLimit-Remaining"]);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        // Verify audit violation was recorded
        _auditLogMock.Verify(a => a.LogViolation(It.Is<RateLimitViolationLog>(v =>
            v.ClientKey == client.ClientKey &&
            v.RequestPath == "/api/products" &&
            v.StatusCode == 429 &&
            v.RetryAfterSeconds == 15)), Times.Once);

        // Verify RFC 7807 response body
        responseStream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseStream);
        var responseBody = await reader.ReadToEndAsync();
        var problemDetails = JsonSerializer.Deserialize<ProblemDetails>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(problemDetails);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problemDetails.Status);
        Assert.Equal("Too Many Requests", problemDetails.Title);
        Assert.Contains("exceeded", problemDetails.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/api/products", problemDetails.Instance);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/swagger")]
    [InlineData("/openapi/v1.json")]
    public async Task InvokeAsync_WhenBypassEndpoint_PassesThroughWithoutRateLimiting(string path)
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = (_) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        // Act
        await middleware.InvokeAsync(context, _resolverMock.Object, _rateLimiterMock.Object, _auditLogMock.Object);

        // Assert
        Assert.True(nextCalled);
        _rateLimiterMock.Verify(r => r.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<RateLimitRule>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
