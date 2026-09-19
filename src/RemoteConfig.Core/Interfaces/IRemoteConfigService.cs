using System.Text.Json;
using RemoteConfig.Core.Models;

namespace RemoteConfig.Core.Interfaces;

/// <summary>
/// High-level service interface for remote configuration management.
/// </summary>
public interface IRemoteConfigService
{
    /// <summary>Get the active config for a device.</summary>
    Task<DeviceConfig?> GetActiveConfigAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Push a new config to a device. Creates a new version.</summary>
    Task<DeviceConfig> PushConfigAsync(
        string deviceId,
        JsonDocument configuration,
        string caller,
        string? description = null,
        Dictionary<string, string>? tags = null,
        CancellationToken ct = default);

    /// <summary>Rollback to a previous config version.</summary>
    Task<DeviceConfig> RollbackAsync(
        string deviceId,
        int targetVersion,
        string reason,
        string caller,
        CancellationToken ct = default);

    /// <summary>List config versions for a device.</summary>
    Task<IReadOnlyList<ConfigVersionInfo>> GetVersionHistoryAsync(
        string deviceId,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default);
}
