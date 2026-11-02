using DistributedRateLimiter.Api.Data;
using DistributedRateLimiter.Api.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistributedRateLimiter.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
public class AuditLogsController : ControllerBase
{
    private readonly RateLimiterDbContext _dbContext;

    public AuditLogsController(RateLimiterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Retrieves recent rate-limit violation audit records from PostgreSQL.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RateLimitViolationLog>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentViolations([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var cappedLimit = Math.Clamp(limit, 1, 500);
        var logs = await _dbContext.RateLimitViolations
            .AsNoTracking()
            .OrderByDescending(v => v.TimestampUtc)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }

    /// <summary>
    /// Retrieves rate-limit violation records filtered by client key (IP or API Key).
    /// </summary>
    [HttpGet("client/{clientKey}")]
    [ProducesResponseType(typeof(IEnumerable<RateLimitViolationLog>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetViolationsByClient(string clientKey, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var cappedLimit = Math.Clamp(limit, 1, 500);
        var logs = await _dbContext.RateLimitViolations
            .AsNoTracking()
            .Where(v => v.ClientKey == clientKey)
            .OrderByDescending(v => v.TimestampUtc)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);

        return Ok(logs);
    }

    /// <summary>
    /// Returns aggregated metrics regarding rate limit violations.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditStats(CancellationToken cancellationToken = default)
    {
        var totalViolations = await _dbContext.RateLimitViolations.CountAsync(cancellationToken);
        var recentViolationsLastHour = await _dbContext.RateLimitViolations
            .Where(v => v.TimestampUtc >= DateTimeOffset.UtcNow.AddHours(-1))
            .CountAsync(cancellationToken);

        var topOffenders = await _dbContext.RateLimitViolations
            .AsNoTracking()
            .GroupBy(v => v.ClientKey)
            .Select(g => new { ClientKey = g.Key, ViolationCount = g.Count() })
            .OrderByDescending(x => x.ViolationCount)
            .Take(5)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            TotalViolations = totalViolations,
            ViolationsLastHour = recentViolationsLastHour,
            TopOffenders = topOffenders
        });
    }
}
