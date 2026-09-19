using RemoteConfig.Core.Models;

namespace RemoteConfig.Core.Interfaces;

/// <summary>
/// Rollback engine: revert a device config to a previous version.
/// </summary>
public interface IConfigRollback
{
    /// <summary>Rollback device to a specific config version. Returns the new active config.</summary>
    Task<DeviceConfig> RollbackAsync(string deviceId, int targetVersion, string reason, string caller, CancellationToken ct = default);

    /// <summary>Get the diff between two config versions.</summary>
    string GetDiff(DeviceConfig oldConfig, DeviceConfig newConfig);
}
