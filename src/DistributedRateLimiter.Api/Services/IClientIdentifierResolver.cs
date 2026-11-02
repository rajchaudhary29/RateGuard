namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// Resolved client identity information for rate limiting decisions.
/// </summary>
/// <param name="ClientKey">Unique cache key representing the client (e.g. apikey:xyz or ip:1.2.3.4).</param>
/// <param name="ClientIp">Remote IP address.</param>
/// <param name="Tier">Assigned client tier (e.g. Anonymous, Standard, Premium).</param>
/// <param name="ApiKey">Optional API key provided by the client.</param>
public record ClientIdentity(string ClientKey, string ClientIp, string Tier, string? ApiKey);

/// <summary>
/// Service contract for resolving client identity and tier from HTTP requests.
/// </summary>
public interface IClientIdentifierResolver
{
    /// <summary>
    /// Resolves the client identity from the current HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A populated <see cref="ClientIdentity"/> object.</returns>
    ClientIdentity ResolveClientIdentity(HttpContext context);
}
