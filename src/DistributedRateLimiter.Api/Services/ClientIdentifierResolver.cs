using DistributedRateLimiter.Api.Options;
using Microsoft.Extensions.Options;

namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// Default implementation of <see cref="IClientIdentifierResolver"/> that inspects API Key headers,
/// Bearer tokens, and client IP addresses (with X-Forwarded-For support).
/// </summary>
public class ClientIdentifierResolver : IClientIdentifierResolver
{
    private readonly RateLimitOptions _options;

    public ClientIdentifierResolver(IOptions<RateLimitOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public ClientIdentity ResolveClientIdentity(HttpContext context)
    {
        var clientIp = ExtractClientIp(context);

        // 1. Check custom API Key header (e.g. X-Api-Key)
        if (context.Request.Headers.TryGetValue(_options.ApiKeyHeaderName, out var apiKeyHeaderVal) &&
            !string.IsNullOrWhiteSpace(apiKeyHeaderVal))
        {
            var apiKey = apiKeyHeaderVal.ToString().Trim();
            var tier = ResolveTierForApiKey(apiKey);
            var clientKey = $"apikey:{apiKey}";
            return new ClientIdentity(clientKey, clientIp, tier, apiKey);
        }

        // 2. Check Authorization Bearer header
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeaderVal) &&
            !string.IsNullOrWhiteSpace(authHeaderVal))
        {
            var authHeader = authHeaderVal.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    var tier = ResolveTierForApiKey(token);
                    var clientKey = $"bearer:{token}";
                    return new ClientIdentity(clientKey, clientIp, tier, token);
                }
            }
        }

        // 3. Fallback to client IP address (Anonymous Tier)
        var anonymousKey = $"ip:{clientIp}";
        return new ClientIdentity(anonymousKey, clientIp, "Anonymous", null);
    }

    private string ResolveTierForApiKey(string apiKey)
    {
        if (_options.ApiKeyTiers.TryGetValue(apiKey, out var tier) && !string.IsNullOrWhiteSpace(tier))
        {
            return tier;
        }

        return "Standard";
    }

    private string ExtractClientIp(HttpContext context)
    {
        if (_options.TrustForwardedHeaders)
        {
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) &&
                !string.IsNullOrWhiteSpace(forwardedFor))
            {
                // First IP in list is original client IP
                var ips = forwardedFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ips.Length > 0 && !string.IsNullOrWhiteSpace(ips[0]))
                {
                    return ips[0];
                }
            }

            if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp) &&
                !string.IsNullOrWhiteSpace(realIp))
            {
                return realIp.ToString().Trim();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    }
}
