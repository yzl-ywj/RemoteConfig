-- ============================================================
-- IoT Remote Config — Database Schema
-- Target: Azure SQL Database
-- ============================================================

CREATE SCHEMA IF NOT EXISTS remote_config;
GO

-- ─── device_configs (current active config per device) ───────
CREATE TABLE remote_config.device_configs (
    device_id           NVARCHAR(128)   NOT NULL PRIMARY KEY,
    active_config_id    UNIQUEIDENTIFIER NOT NULL,
    config_version      INT             NOT NULL DEFAULT 1,
    configuration       NVARCHAR(MAX)   NOT NULL CHECK (ISJSON(configuration) = 1),
    description         NVARCHAR(500)   NULL,
    changed_by          NVARCHAR(256)   NOT NULL DEFAULT 'system',
    status              TINYINT         NOT NULL DEFAULT 1,  -- ConfigStatus enum
    previous_version    INT             NULL,
    rollback_reason     NVARCHAR(500)   NULL,
    tags                NVARCHAR(MAX)   NULL,  -- JSON dict
    created_at          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    
    CONSTRAINT fk_device_configs_config FOREIGN KEY (active_config_id) 
        REFERENCES remote_config.config_versions(config_id)
);

-- ─── config_versions (full version history) ───────────────────
CREATE TABLE remote_config.config_versions (
    config_id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    device_id           NVARCHAR(128)   NOT NULL,
    version             INT             NOT NULL,
    configuration       NVARCHAR(MAX)   NOT NULL CHECK (ISJSON(configuration) = 1),
    description         NVARCHAR(500)   NULL,
    changed_by          NVARCHAR(256)   NOT NULL DEFAULT 'system',
    status              TINYINT         NOT NULL DEFAULT 1,
    previous_version    INT             NULL,
    rollback_reason     NVARCHAR(500)   NULL,
    created_at          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    
    CONSTRAINT uq_config_device_version UNIQUE (device_id, version)
);

CREATE INDEX ix_config_versions_device ON remote_config.config_versions(device_id, version DESC);
CREATE INDEX ix_config_versions_status ON remote_config.config_versions(status);

-- ─── commands (command request log) ───────────────────────────
CREATE TABLE remote_config.commands (
    command_id          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    device_id           NVARCHAR(128)   NOT NULL,
    group_id            UNIQUEIDENTIFIER NULL,
    command_type        TINYINT         NOT NULL,
    payload             NVARCHAR(MAX)   NULL,
    delivery_mode       TINYINT         NOT NULL DEFAULT 0,
    timeout_seconds     INT             NOT NULL DEFAULT 30,
    max_retries         INT             NOT NULL DEFAULT 3,
    status              TINYINT         NOT NULL DEFAULT 0,
    result_message      NVARCHAR(MAX)   NULL,
    error_message       NVARCHAR(MAX)   NULL,
    issued_by           NVARCHAR(256)   NOT NULL DEFAULT 'system',
    created_at          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    sent_at             DATETIME2       NULL,
    acked_at            DATETIME2       NULL,
    retry_count         INT             NOT NULL DEFAULT 0,
    correlation_id      NVARCHAR(128)   NULL
);

CREATE INDEX ix_commands_device ON remote_config.commands(device_id, created_at DESC);
CREATE INDEX ix_commands_status ON remote_config.commands(status, created_at DESC);
CREATE INDEX ix_commands_correlation ON remote_config.commands(correlation_id);

-- ─── audit_log (append-only) ──────────────────────────────────
CREATE TABLE remote_config.audit_log (
    audit_id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    device_id           NVARCHAR(128)   NOT NULL,
    action              NVARCHAR(50)    NOT NULL,
    changed_by          NVARCHAR(256)   NOT NULL,
    old_values          NVARCHAR(MAX)   NULL CHECK (old_values IS NULL OR ISJSON(old_values) = 1),
    new_values          NVARCHAR(MAX)   NULL CHECK (new_values IS NULL OR ISJSON(new_values) = 1),
    metadata            NVARCHAR(MAX)   NULL,  -- JSON dict
    timestamp           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    correlation_id      NVARCHAR(128)   NULL
);

CREATE INDEX ix_audit_device ON remote_config.audit_log(device_id, timestamp DESC);
CREATE INDEX ix_audit_action ON remote_config.audit_log(action, timestamp DESC);
CREATE INDEX ix_audit_correlation ON remote_config.audit_log(correlation_id);

-- ─── Stored Procedure: Get config diff ───────────────────────
CREATE OR ALTER PROCEDURE remote_config.sp_get_config_diff
    @deviceId NVARCHAR(128),
    @versionA INT,
    @versionB INT
AS
BEGIN
    SELECT 
        a.configuration AS config_a,
        b.configuration AS config_b,
        a.version AS version_a,
        b.version AS version_b
    FROM remote_config.config_versions a
    FULL OUTER JOIN remote_config.config_versions b
        ON a.device_id = b.device_id
    WHERE a.device_id = @deviceId AND a.version = @versionA
      AND b.device_id = @deviceId AND b.version = @versionB;
END;
GO

-- ─── Stored Procedure: Cleanup old versions ──────────────────
CREATE OR ALTER PROCEDURE remote_config.sp_cleanup_old_versions
    @deviceId NVARCHAR(128),
    @keepCount INT = 50
AS
BEGIN
    DELETE FROM remote_config.config_versions
    WHERE device_id = @deviceId
      AND version NOT IN (
          SELECT TOP (@keepCount) version 
          FROM remote_config.config_versions 
          WHERE device_id = @deviceId 
          ORDER BY version DESC
      )
      AND status NOT IN (1); -- Don't delete active
END;
GO
