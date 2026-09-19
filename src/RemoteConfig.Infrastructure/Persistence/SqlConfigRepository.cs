using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Infrastructure.Persistence;

/// <summary>
/// SQL Server / Azure SQL implementation of IConfigRepository.
/// Uses Dapper for lightweight, high-performance data access.
/// </summary>
public class SqlConfigRepository : IConfigRepository
{
    private readonly DatabaseOptions _dbOptions;
    private readonly ILogger<SqlConfigRepository> _logger;

    public SqlConfigRepository(
        IOptions<DatabaseOptions> dbOptions,
        ILogger<SqlConfigRepository> logger)
    {
        _dbOptions = dbOptions.Value;
        _logger = logger;
    }

    private IDbConnection CreateConnection()
    {
        var conn = new SqlConnection(_dbOptions.ConnectionString);
        conn.Open();
        return conn;
    }

    public async Task<DeviceConfig?> GetActiveAsync(string deviceId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT cv.config_id, cv.device_id, cv.version, cv.configuration,
                   cv.description, cv.changed_by, cv.status, cv.previous_version,
                   cv.rollback_reason, cv.created_at, cv.created_at as updated_at
            FROM remote_config.device_configs dc
            INNER JOIN remote_config.config_versions cv ON dc.active_config_id = cv.config_id
            WHERE dc.device_id = @DeviceId";

        using var conn = CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<ConfigRow>(sql, new { DeviceId = deviceId });
        return row == null ? null : MapToDeviceConfig(row);
    }

    public async Task<DeviceConfig?> GetByIdAsync(Guid configId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT config_id, device_id, version, configuration,
                   description, changed_by, status, previous_version,
                   rollback_reason, created_at, created_at as updated_at
            FROM remote_config.config_versions
            WHERE config_id = @ConfigId";

        using var conn = CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<ConfigRow>(sql, new { ConfigId = configId });
        return row == null ? null : MapToDeviceConfig(row);
    }

    public async Task<IReadOnlyList<DeviceConfig>> GetVersionsAsync(
        string deviceId, int skip = 0, int take = 20, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT config_id, device_id, version, configuration,
                   description, changed_by, status, previous_version,
                   rollback_reason, created_at, created_at as updated_at
            FROM remote_config.config_versions
            WHERE device_id = @DeviceId
            ORDER BY version DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<ConfigRow>(sql, new { DeviceId = deviceId, Skip = skip, Take = take });
        return rows.Select(MapToDeviceConfig).ToList().AsReadOnly();
    }

    public async Task<DeviceConfig> CreateAsync(DeviceConfig config, CancellationToken ct = default)
    {
        // Determine next version number
        const string getMaxVersion = @"
            SELECT ISNULL(MAX(version), 0) FROM remote_config.config_versions WHERE device_id = @DeviceId";

        using var conn = CreateConnection();
        var maxVersion = await conn.ExecuteScalarAsync<int>(getMaxVersion, new { DeviceId = config.DeviceId });
        var newVersion = maxVersion + 1;
        config.Version = newVersion;
        config.CreatedAt = DateTime.UtcNow;
        config.UpdatedAt = DateTime.UtcNow;

        const string insertVersion = @"
            INSERT INTO remote_config.config_versions
                (config_id, device_id, version, configuration, description, changed_by, status, previous_version, rollback_reason, created_at)
            VALUES
                (@ConfigId, @DeviceId, @Version, @Configuration, @Description, @ChangedBy, @Status, @PreviousVersion, @RollbackReason, @CreatedAt)";

        await conn.ExecuteAsync(insertVersion, new
        {
            ConfigId = config.ConfigId,
            DeviceId = config.DeviceId,
            Version = config.Version,
            Configuration = config.Configuration.RootElement.GetRawText(),
            Description = config.Description,
            ChangedBy = config.ChangedBy,
            Status = (int)config.Status,
            PreviousVersion = config.PreviousVersion,
            RollbackReason = config.RollbackReason,
            CreatedAt = config.CreatedAt
        });

        // Upsert device_configs (current active)
        const string upsertActive = @"
            MERGE remote_config.device_configs AS target
            USING (SELECT @DeviceId AS device_id) AS source
            ON target.device_id = source.device_id
            WHEN MATCHED THEN
                UPDATE SET active_config_id = @ConfigId,
                           config_version = @Version,
                           configuration = @Configuration,
                           description = @Description,
                           changed_by = @ChangedBy,
                           status = @Status,
                           previous_version = @PreviousVersion,
                           updated_at = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (device_id, active_config_id, config_version, configuration, description, changed_by, status, previous_version)
                VALUES (@DeviceId, @ConfigId, @Version, @Configuration, @Description, @ChangedBy, @Status, @PreviousVersion);";

        await conn.ExecuteAsync(upsertActive, new
        {
            DeviceId = config.DeviceId,
            ConfigId = config.ConfigId,
            Version = config.Version,
            Configuration = config.Configuration.RootElement.GetRawText(),
            Description = config.Description,
            ChangedBy = config.ChangedBy,
            Status = (int)config.Status,
            PreviousVersion = config.PreviousVersion
        });

        _logger.LogInformation(
            "Config version {Version} created for device {DeviceId}, configId={ConfigId}",
            config.Version, config.DeviceId, config.ConfigId);

        return config;
    }

    public async Task<bool> UpdateStatusAsync(
        Guid configId, Core.Enums.ConfigStatus status, string? reason = null, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE remote_config.config_versions
            SET status = @Status, rollback_reason = @Reason
            WHERE config_id = @ConfigId;
            
            UPDATE remote_config.device_configs
            SET status = @Status
            WHERE active_config_id = @ConfigId";

        using var conn = CreateConnection();
        var rows = await conn.ExecuteAsync(sql, new
        {
            ConfigId = configId,
            Status = (int)status,
            Reason = reason
        });

        return rows > 0;
    }

    public async Task<int> GetVersionCountAsync(string deviceId, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(*) FROM remote_config.config_versions WHERE device_id = @DeviceId";
        using var conn = CreateConnection();
        return await conn.ExecuteScalarAsync<int>(sql, new { DeviceId = deviceId });
    }

    // ─── Helper: map DB row → DeviceConfig ──────────────────
    private static DeviceConfig MapToDeviceConfig(ConfigRow row)
    {
        return new DeviceConfig
        {
            ConfigId = row.config_id,
            DeviceId = row.device_id,
            Version = row.version,
            Configuration = System.Text.Json.JsonDocument.Parse(row.configuration),
            Description = row.description,
            ChangedBy = row.changed_by,
            Status = (Core.Enums.ConfigStatus)row.status,
            PreviousVersion = row.previous_version,
            RollbackReason = row.rollback_reason,
            CreatedAt = row.created_at,
            UpdatedAt = row.updated_at
        };
    }

    // ─── Internal DTO for Dapper mapping ─────────────────────
    private class ConfigRow
    {
        public Guid config_id { get; set; }
        public string device_id { get; set; } = string.Empty;
        public int version { get; set; }
        public string configuration { get; set; } = string.Empty;
        public string? description { get; set; }
        public string changed_by { get; set; } = string.Empty;
        public int status { get; set; }
        public int? previous_version { get; set; }
        public string? rollback_reason { get; set; }
        public DateTime created_at { get; set; }
        public DateTime updated_at { get; set; }
    }
}
