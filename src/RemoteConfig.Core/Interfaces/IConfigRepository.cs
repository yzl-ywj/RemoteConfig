using RemoteConfig.Core.Models;
using RemoteConfig.Core.Enums;

namespace RemoteConfig.Core.Interfaces;

/// <summary>
/// Repository interface for device configuration persistence.
/// </summary>
public interface IConfigRepository
{
    /// <summary>Get the active config for a device.</summary>
    Task<DeviceConfig?> GetActiveAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Get a specific config version by ID.</summary>
    Task<DeviceConfig?> GetByIdAsync(Guid configId, CancellationToken ct = default);

    /// <summary>List all versions for a device (newest first).</summary>
    Task<IReadOnlyList<DeviceConfig>> GetVersionsAsync(string deviceId, int skip = 0, int take = 20, CancellationToken ct = default);

    /// <summary>Create a new config version (sets Version = max+1).</summary>
    Task<DeviceConfig> CreateAsync(DeviceConfig config, CancellationToken ct = default);

    /// <summary>Update config status (Supersede/RolledBack/Failed).</summary>
    Task<bool> UpdateStatusAsync(Guid configId, ConfigStatus status, string? reason = null, CancellationToken ct = default);

    /// <summary>Get total version count for a device.</summary>
    Task<int> GetVersionCountAsync(string deviceId, CancellationToken ct = default);
}
