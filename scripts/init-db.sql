-- Idempotent PostgreSQL schema initialization for DistributedRateLimiter
-- Can be executed safely against any PostgreSQL database or as a Docker / Kubernetes entrypoint script.

CREATE TABLE IF NOT EXISTS rate_limit_violation_logs (
    "Id" BIGSERIAL PRIMARY KEY,
    "ClientKey" VARCHAR(256) NOT NULL,
    "ClientIp" VARCHAR(64) DEFAULT '',
    "Tier" VARCHAR(64) DEFAULT '',
    "RequestPath" VARCHAR(512) NOT NULL,
    "HttpMethod" VARCHAR(16) NOT NULL,
    "StatusCode" INT NOT NULL DEFAULT 429,
    "PermitLimit" INT NOT NULL,
    "RequestCount" BIGINT NOT NULL,
    "RetryAfterSeconds" INT NOT NULL,
    "QueryString" VARCHAR(2048),
    "UserAgent" VARCHAR(512),
    "CorrelationId" VARCHAR(128),
    "TimestampUtc" TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Optimized B-Tree indexes for audit log queries and rate limit analytics
CREATE INDEX IF NOT EXISTS ix_violation_client_key 
    ON rate_limit_violation_logs ("ClientKey");

CREATE INDEX IF NOT EXISTS ix_violation_timestamp_utc 
    ON rate_limit_violation_logs ("TimestampUtc");

CREATE INDEX IF NOT EXISTS ix_violation_client_timestamp 
    ON rate_limit_violation_logs ("ClientKey", "TimestampUtc");

CREATE INDEX IF NOT EXISTS ix_violation_request_path 
    ON rate_limit_violation_logs ("RequestPath");
