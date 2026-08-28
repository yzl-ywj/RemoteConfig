using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Infrastructure.Services;

/// <summary>
/// Core business logic for remote configuration management.
/// Handles config push, versioning, rollback, and audit logging.
/// </summary>
public class RemoteConfigService : IRemoteConfigService
{
    private readonly IConfigRepository _configRepo;
    private readonly IAuditLogger _auditLogger;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly ILogger<RemoteConfigService> _logger;
    private readonly RemoteConfigOptions _options;

    public RemoteConfigService(
        IConfigRepository configRepo,
        IAuditLogger auditLogger,
        ICommandDispatcher commandDispatcher,
        IOptions<RemoteConfigOptions> options,
        ILogger<RemoteConfigService> logger)
    {
        _configRepo = configRepo;
        _auditLogger = auditLogger;
        _commandDispatcher = commandDispatcher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DeviceConfig?> GetActiveConfigAsync(string deviceId, CancellationToken ct = default)
    {
        return await _configRepo.GetActiveAsync(deviceId, ct);
    }

    public async Task<DeviceConfig> PushConfigAsync(
        string deviceId,
        JsonDocument configuration,
        string caller,
        string? description = null,
        Dictionary<string, string>? tags = null,
        CancellationToken ct = default)
    {
        // Validate payload size
        var payloadSize = configuration.RootElement.GetRawText().Length;
        if (payloadSize > _options.MaxConfigSizeBytes)
        {
            throw new InvalidOperationException(
                $"Config payload too large: {payloadSize} bytes (max {_options.MaxConfigSizeBytes})");
        }

        // Get current active config for audit
        var current = await _configRepo.GetActiveAsync(deviceId, ct);

        // Create new version
        var newConfig = new DeviceConfig
        {
            ConfigId = Guid.NewGuid(),
            DeviceId = deviceId,
            Configuration = configuration,
            Description = description,
            ChangedBy = caller,
            Status = ConfigStatus.Active,
            PreviousVersion = current?.Version,
            Tags = tags ?? new Dictionary<string, string>()
        };

        var created = await _configRepo.CreateAsync(newConfig, ct);

        // Mark old config as superseded
        if (current != null && current.ConfigId != created.ConfigId)
        {
            await _configRepo.UpdateStatusAsync(current.ConfigId, ConfigStatus.Superseded, ct: ct);
        }

        // Audit log
        await _auditLogger.WriteAsync(new AuditEntry
        {
            DeviceId = deviceId,
            Action = "CONFIG_PUSH",
            ChangedBy = caller,
            OldValues = current?.Configuration,
            NewValues = configuration,
            Metadata = new Dictionary<string, string>
            {
                ["configId"] = created.ConfigId.ToString(),
                ["version"] = created.Version.ToString(),
                ["previousVersion"] = (current?.Version.ToString() ?? "none")
            }
        }, ct);

        // Prune old versions if exceeding max
        var count = await _configRepo.GetVersionCountAsync(deviceId, ct);
        if (count > _options.MaxVersionsPerDevice)
        {
            _logger.LogInformation(
                "Device {DeviceId} has {Count} versions, exceeding max {Max}. Old versions should be pruned.",
                deviceId, count, _options.MaxVersionsPerDevice);
            // Pruning is handled by stored procedure sp_cleanup_old_versions
        }

        _logger.LogInformation(
            "Config v{Version} pushed to {DeviceId} by {Caller}",
            created.Version, deviceId, caller);

        return created;
    }

    public async Task<DeviceConfig> RollbackAsync(
        string deviceId,
        int targetVersion,
        string reason,
        string caller,
        CancellationToken ct = default)
    {
        // Get target version
        var versions = await _configRepo.GetVersionsAsync(deviceId, 0, 100, ct);
        var target = versions.FirstOrDefault(v => v.Version == targetVersion)
                     ?? throw new InvalidOperationException($"Version {targetVersion} not found for device {deviceId}");

        // Get current for audit
        var current = await _configRepo.GetActiveAsync(deviceId, ct);

        // Push the old config as a new version (proper versioning)
        var rollbackConfig = JsonDocument.Parse(target.Configuration.RootElement.GetRawText());
        
        var newVersion = await PushConfigAsync(
            deviceId,
            rollbackConfig,
            caller,
            description: $"Rollback to v{targetVersion}: {reason}",
            ct: ct);

        // Mark the rollback target as RolledBack (the intermediate versions stay Superseded)
        await _configRepo.UpdateStatusAsync(target.ConfigId, ConfigStatus.RolledBack, reason, ct);

        // Audit
        await _auditLogger.WriteAsync(new AuditEntry
        {
            DeviceId = deviceId,
            Action = "CONFIG_ROLLBACK",
            ChangedBy = caller,
            OldValues = current?.Configuration,
            NewValues = rollbackConfig,
            Metadata = new Dictionary<string, string>
            {
                ["targetVersion"] = targetVersion.ToString(),
                ["newVersion"] = newVersion.Version.ToString(),
                ["reason"] = reason
            }
        }, ct);

        _logger.LogWarning(
            "Config rolled back: {DeviceId} → v{TargetVersion} by {Caller} ({Reason})",
            deviceId, targetVersion, caller, reason);

        return newVersion;
    }

    public async Task<IReadOnlyList<ConfigVersionInfo>> GetVersionHistoryAsync(
        string deviceId,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default)
    {
        var versions = await _configRepo.GetVersionsAsync(deviceId, skip, take, ct);
        return versions.Select(v => new ConfigVersionInfo
        {
            ConfigId = v.ConfigId,
            Version = v.Version,
            Status = v.Status,
            Description = v.Description,
            ChangedBy = v.ChangedBy,
            CreatedAt = v.CreatedAt,
            PreviousVersion = v.PreviousVersion
        }).ToList().AsReadOnly();
    }
}
