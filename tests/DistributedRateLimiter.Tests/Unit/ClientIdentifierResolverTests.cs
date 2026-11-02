using System.Net;
using DistributedRateLimiter.Api.Options;
using DistributedRateLimiter.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DistributedRateLimiter.Tests.Unit;

public class ClientIdentifierResolverTests
{
    private readonly RateLimitOptions _options;

    public ClientIdentifierResolverTests()
    {
        _options = new RateLimitOptions
        {
            ApiKeyHeaderName = "X-Api-Key",
            TrustForwardedHeaders = true,
            ApiKeyTiers = new Dictionary<string, string>
            {
                ["key-standard"] = "Standard",
                ["key-premium"] = "Premium"
            }
        };
    }

    private ClientIdentifierResolver CreateResolver()
    {
        return new ClientIdentifierResolver(Options.Create(_options));
    }

    [Fact]
    public void ResolveClientIdentity_WhenApiKeyHeaderPresent_ReturnsApiKeyIdentityAndTier()
    {
        // Arrange
        var resolver = CreateResolver();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "key-premium";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");

        // Act
        var identity = resolver.ResolveClientIdentity(context);

        // Assert
        Assert.Equal("apikey:key-premium", identity.ClientKey);
        Assert.Equal("Premium", identity.Tier);
        Assert.Equal("key-premium", identity.ApiKey);
        Assert.Equal("192.168.1.100", identity.ClientIp);
    }

    [Fact]
    public void ResolveClientIdentity_WhenUnknownApiKey_DefaultsToStandardTier()
    {
        // Arrange
        var resolver = CreateResolver();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "unknown-key-123";

        // Act
        var identity = resolver.ResolveClientIdentity(context);

        // Assert
        Assert.Equal("apikey:unknown-key-123", identity.ClientKey);
        Assert.Equal("Standard", identity.Tier);
        Assert.Equal("unknown-key-123", identity.ApiKey);
    }

    [Fact]
    public void ResolveClientIdentity_WhenBearerTokenPresent_ResolvesBearerClientKey()
    {
        // Arrange
        var resolver = CreateResolver();
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = "Bearer token-abc-xyz";

        // Act
        var identity = resolver.ResolveClientIdentity(context);

        // Assert
        Assert.Equal("bearer:token-abc-xyz", identity.ClientKey);
        Assert.Equal("token-abc-xyz", identity.ApiKey);
    }

    [Fact]
    public void ResolveClientIdentity_WhenForwardedForHeaderPresent_ExtractsFirstIp()
    {
        // Arrange
        var resolver = CreateResolver();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.195, 70.41.3.18, 150.172.238.178";

        // Act
        var identity = resolver.ResolveClientIdentity(context);

        // Assert
        Assert.Equal("ip:203.0.113.195", identity.ClientKey);
        Assert.Equal("203.0.113.195", identity.ClientIp);
        Assert.Equal("Anonymous", identity.Tier);
        Assert.Null(identity.ApiKey);
    }

    [Fact]
    public void ResolveClientIdentity_WhenNoHeadersProvided_FallsBackToRemoteIp()
    {
        // Arrange
        var resolver = CreateResolver();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        // Act
        var identity = resolver.ResolveClientIdentity(context);

        // Assert
        Assert.Equal("ip:10.0.0.5", identity.ClientKey);
        Assert.Equal("10.0.0.5", identity.ClientIp);
        Assert.Equal("Anonymous", identity.Tier);
    }
}
