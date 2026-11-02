using DistributedRateLimiter.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistributedRateLimiter.Api.Data;

/// <summary>
/// PostgreSQL DbContext for persisting rate limit violations and audit metadata.
/// </summary>
public class RateLimiterDbContext : DbContext
{
    public RateLimiterDbContext(DbContextOptions<RateLimiterDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Audited violation records.
    /// </summary>
    public DbSet<RateLimitViolationLog> RateLimitViolations => Set<RateLimitViolationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RateLimitViolationLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ClientKey)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.ClientIp)
                .HasMaxLength(64);

            entity.Property(e => e.Tier)
                .HasMaxLength(64);

            entity.Property(e => e.RequestPath)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(e => e.HttpMethod)
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(e => e.QueryString)
                .HasMaxLength(2048);

            entity.Property(e => e.UserAgent)
                .HasMaxLength(512);

            entity.Property(e => e.CorrelationId)
                .HasMaxLength(128);

            // Indexes for fast audit retrieval and analytical aggregations
            entity.HasIndex(e => e.ClientKey)
                .HasDatabaseName("ix_violation_client_key");

            entity.HasIndex(e => e.TimestampUtc)
                .HasDatabaseName("ix_violation_timestamp_utc");

            entity.HasIndex(e => new { e.ClientKey, e.TimestampUtc })
                .HasDatabaseName("ix_violation_client_timestamp");

            entity.HasIndex(e => e.RequestPath)
                .HasDatabaseName("ix_violation_request_path");
        });
    }
}
