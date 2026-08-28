using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;
using StackExchange.Redis;

namespace RemoteConfig.Infrastructure.Messaging;

/// <summary>
/// Redis-backed command store for fast pending-command lookups and ACK tracking.
/// Uses Hash for command data + TTL for auto-expiry of stale commands.
/// </summary>
public class RedisCommandStore : ICommandStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisCommandStore> _logger;
    private readonly IDatabase _db;

    public RedisCommandStore(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisCommandStore> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
        _db = _redis.GetDatabase();
    }

    private string Key(Guid commandId) => $"{_options.InstanceName}:{_options.RedisKeyPrefix}{commandId}";
    private string DeviceIndexKey(string deviceId) => $"{_options.InstanceName}:device-cmds:{deviceId}";

    public async Task SaveAsync(CommandRequest command, CancellationToken ct = default)
    {
        var key = Key(command.CommandId);
        var json = JsonSerializer.Serialize(command, JsonOptions());
        var ttl = TimeSpan.FromSeconds(Math.Max(command.TimeoutSeconds * 2, 60));

        var tran = _db.CreateTransaction();
        tran.StringSetAsync(key, json, ttl);
        tran.SetAddAsync(DeviceIndexKey(command.DeviceId), command.CommandId.ToString());
        tran.KeyExpireAsync(DeviceIndexKey(command.DeviceId), TimeSpan.FromDays(1));
        await tran.ExecuteAsync();

        _logger.LogDebug("Saved pending command {CommandId} to Redis (TTL={Ttl}s)", command.CommandId, ttl.TotalSeconds);
    }

    public async Task<CommandRequest?> GetByIdAsync(Guid commandId, CancellationToken ct = default)
    {
        var json = await _db.StringGetAsync(Key(commandId));
        if (json.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<CommandRequest>(json!, JsonOptions());
    }

    public async Task<IReadOnlyList<CommandRequest>> GetPendingForDeviceAsync(
        string deviceId, CancellationToken ct = default)
    {
        var members = await _db.SetMembersAsync(DeviceIndexKey(deviceId));
        var results = new List<CommandRequest>();

        foreach (var m in members)
        {
            if (Guid.TryParse(m.ToString(), out var id))
            {
                var cmd = await GetByIdAsync(id, ct);
                if (cmd != null) results.Add(cmd);
            }
        }

        return results.AsReadOnly();
    }

    public async Task RemoveAsync(Guid commandId, CancellationToken ct = default)
    {
        // Look up device_id first
        var cmd = await GetByIdAsync(commandId, ct);
        var tran = _db.CreateTransaction();
        tran.KeyDeleteAsync(Key(commandId));
        if (cmd != null)
            tran.SetRemoveAsync(DeviceIndexKey(cmd.DeviceId), commandId.ToString());
        await tran.ExecuteAsync();
        _logger.LogDebug("Removed command {CommandId} from Redis", commandId);
    }

    public async Task<IReadOnlyList<CommandRequest>> SearchAsync(
        string? deviceId = null,
        Core.Enums.CommandStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        // Redis is not ideal for complex queries; this is a fallback.
        // In production, the SQL CommandStore is the primary search backend.
        // This implementation scans the device index if deviceId is provided.
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            return await GetPendingForDeviceAsync(deviceId, ct);
        }

        // Otherwise return empty — search should use SQL
        return new List<CommandRequest>().AsReadOnly();
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }
}
