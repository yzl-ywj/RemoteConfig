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
/// Append-only audit logger backed by Azure SQL.
/// Entries are NEVER updated or deleted — only inserted.
/// </summary>
public class SqlAuditLogger : IAuditLogger
{
    private readonly DatabaseOptions _dbOptions;
    private readonly ILogger<SqlAuditLogger> _logger;

    public SqlAuditLogger(
        IOptions<DatabaseOptions> dbOptions,
        ILogger<SqlAuditLogger> logger)
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

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO remote_config.audit_log
                (audit_id, device_id, action, changed_by, old_values, new_values, metadata, timestamp, correlation_id)
            VALUES
                (@AuditId, @DeviceId, @Action, @ChangedBy, @OldValues, @NewValues, @Metadata, @Timestamp, @CorrelationId)";

        using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            entry.AuditId,
            entry.DeviceId,
            entry.Action,
            entry.ChangedBy,
            OldValues = entry.OldValues?.RootElement.GetRawText(),
            NewValues = entry.NewValues?.RootElement.GetRawText(),
            Metadata = entry.Metadata == null ? null : System.Text.Json.JsonSerializer.Serialize(entry.Metadata),
            entry.Timestamp,
            entry.CorrelationId
        });

        _logger.LogInformation(
            "Audit: {Action} on {DeviceId} by {ChangedBy} (corr={CorrelationId})",
            entry.Action, entry.DeviceId, entry.ChangedBy, entry.CorrelationId);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetHistoryAsync(
        string deviceId, int limit = 100, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@Limit) *
            FROM remote_config.audit_log
            WHERE device_id = @DeviceId
            ORDER BY timestamp DESC";

        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<AuditRow>(sql, new { DeviceId = deviceId, Limit = Math.Min(limit, 1000) });
        return rows.Select(MapToEntry).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByConfigIdAsync(
        Guid configId, CancellationToken ct = default)
    {
        // Search metadata for config_id reference
        const string sql = @"
            SELECT TOP 100 *
            FROM remote_config.audit_log
            WHERE metadata LIKE '%' + @ConfigId + '%'
               OR old_values LIKE '%' + @ConfigId + '%'
               OR new_values LIKE '%' + @ConfigId + '%'
            ORDER BY timestamp DESC";

        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<AuditRow>(sql, new { ConfigId = configId.ToString() });
        return rows.Select(MapToEntry).ToList().AsReadOnly();
    }

    private static AuditEntry MapToEntry(AuditRow row)
    {
        return new AuditEntry
        {
            AuditId = row.audit_id,
            DeviceId = row.device_id,
            Action = row.action,
            ChangedBy = row.changed_by,
            OldValues = string.IsNullOrEmpty(row.old_values) ? null : System.Text.Json.JsonDocument.Parse(row.old_values),
            NewValues = string.IsNullOrEmpty(row.new_values) ? null : System.Text.Json.JsonDocument.Parse(row.new_values),
            Metadata = string.IsNullOrEmpty(row.metadata) ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(row.metadata),
            Timestamp = row.timestamp,
            CorrelationId = row.correlation_id
        };
    }

    private class AuditRow
    {
        public Guid audit_id { get; set; }
        public string device_id { get; set; } = string.Empty;
        public string action { get; set; } = string.Empty;
        public string changed_by { get; set; } = string.Empty;
        public string? old_values { get; set; }
        public string? new_values { get; set; }
        public string? metadata { get; set; }
        public DateTime timestamp { get; set; }
        public string? correlation_id { get; set; }
    }
}
