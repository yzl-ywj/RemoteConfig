using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;
using CommandType=RemoteConfig.Core.Enums.CommandType;

namespace RemoteConfig.Infrastructure.Persistence;

/// <summary>
/// SQL implementation of ICommandStore.
/// Persists all commands and supports filtered search.
/// </summary>
public class SqlCommandStore : ICommandStore
{
    private readonly DatabaseOptions _dbOptions;
    private readonly ILogger<SqlCommandStore> _logger;

    public SqlCommandStore(
        IOptions<DatabaseOptions> dbOptions,
        ILogger<SqlCommandStore> logger)
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

    public async Task SaveAsync(CommandRequest command, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO remote_config.commands
                (command_id, device_id, group_id, command_type, payload, delivery_mode,
                 timeout_seconds, max_retries, status, result_message, error_message,
                 issued_by, created_at, sent_at, acked_at, retry_count, correlation_id)
            VALUES
                (@CommandId, @DeviceId, @GroupId, @CommandType, @Payload, @DeliveryMode,
                 @TimeoutSeconds, @MaxRetries, @Status, @ResultMessage, @ErrorMessage,
                 @IssuedBy, @CreatedAt, @SentAt, @AckedAt, @RetryCount, @CorrelationId)";

        using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            command.CommandId,
            command.DeviceId,
            command.GroupId,
            CommandType = (int)command.CommandType,
            command.Payload,
            DeliveryMode = (int)command.DeliveryMode,
            command.TimeoutSeconds,
            command.MaxRetries,
            Status = (int)command.Status,
            command.ResultMessage,
            command.ErrorMessage,
            command.IssuedBy,
            command.CreatedAt,
            command.SentAt,
            command.AckedAt,
            command.RetryCount,
            CorrelationId = (string?)null
        });
    }

    public async Task<CommandRequest?> GetByIdAsync(Guid commandId, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM remote_config.commands WHERE command_id = @CommandId";
        using var conn = CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<CommandRow>(sql, new { CommandId = commandId });
        return row == null ? null : MapToCommand(row);
    }

    public async Task<IReadOnlyList<CommandRequest>> GetPendingForDeviceAsync(
        string deviceId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM remote_config.commands
            WHERE device_id = @DeviceId
              AND status IN (0, 1)  -- Queued, Sent
            ORDER BY created_at ASC";

        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<CommandRow>(sql, new { DeviceId = deviceId });
        return rows.Select(MapToCommand).ToList().AsReadOnly();
    }

    public async Task RemoveAsync(Guid commandId, CancellationToken ct = default)
    {
        // We don't delete — we update status to Cancelled/Completed
        const string sql = @"
            UPDATE remote_config.commands
            SET status = 5  -- Cancelled
            WHERE command_id = @CommandId AND status IN (0, 1)";

        using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new { CommandId = commandId });
    }

    public async Task<IReadOnlyList<CommandRequest>> SearchAsync(
        string? deviceId = null,
        CommandStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        var param = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(deviceId)) { where.Add("device_id = @DeviceId"); param.Add("DeviceId", deviceId); }
        if (status.HasValue) { where.Add("status = @Status"); param.Add("Status", (int)status.Value); }
        if (from.HasValue) { where.Add("created_at >= @From"); param.Add("From", from.Value); }
        if (to.HasValue) { where.Add("created_at <= @To"); param.Add("To", to.Value); }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var sql = $@"
            SELECT * FROM remote_config.commands
            {whereClause}
            ORDER BY created_at DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        param.Add("Skip", skip);
        param.Add("Take", Math.Min(take, 500));

        using var conn = CreateConnection();
        var rows = await conn.QueryAsync<CommandRow>(sql, param);
        return rows.Select(MapToCommand).ToList().AsReadOnly();
    }

    // ─── Helper ─────────────────────────────────────────────
    private static CommandRequest MapToCommand(CommandRow row)
    {
        return new CommandRequest
        {
            CommandId = row.command_id,
            DeviceId = row.device_id,
            GroupId = row.group_id,
            CommandType = (CommandType)row.command_type,
            Payload = row.payload,
            DeliveryMode = (CommandDeliveryMode)row.delivery_mode,
            TimeoutSeconds = row.timeout_seconds,
            MaxRetries = row.max_retries,
            Status = (CommandStatus)row.status,
            ResultMessage = row.result_message,
            IssuedBy = row.issued_by,
            CreatedAt = row.created_at,
            SentAt = row.sent_at,
            AckedAt = row.acked_at,
            RetryCount = row.retry_count,
            ErrorMessage = row.error_message
        };
    }

    private class CommandRow
    {
        public Guid command_id { get; set; }
        public string device_id { get; set; } = string.Empty;
        public Guid? group_id { get; set; }
        public int command_type { get; set; }
        public string? payload { get; set; }
        public int delivery_mode { get; set; }
        public int timeout_seconds { get; set; }
        public int max_retries { get; set; }
        public int status { get; set; }
        public string? result_message { get; set; }
        public string? error_message { get; set; }
        public string issued_by { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime? sent_at { get; set; }
        public DateTime? acked_at { get; set; }
        public int retry_count { get; set; }
        public string? correlation_id { get; set; }
    }
}
