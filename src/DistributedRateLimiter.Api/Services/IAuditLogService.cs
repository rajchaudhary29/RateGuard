using DistributedRateLimiter.Api.Models.Entities;

namespace DistributedRateLimiter.Api.Services;

/// <summary>
/// Service interface for buffering and persisting rate limit violation audit records.
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Enqueues a rate limit violation record for asynchronous batch persistence into PostgreSQL.
    /// </summary>
    /// <param name="violation">The violation log entry.</param>
    void LogViolation(RateLimitViolationLog violation);
}
