namespace RemoteConfig.Core.Enums;

/// <summary>
/// Lifecycle status of a device configuration version.
/// </summary>
public enum ConfigStatus
{
    /// <summary>Config is being prepared, not yet pushed to device.</summary>
    Draft = 0,

    /// <summary>Config is the active desired state for the device.</summary>
    Active = 1,

    /// <summary>Config was superseded by a newer version.</summary>
    Superseded = 2,

    /// <summary>Config was rolled back; no longer active.</summary>
    RolledBack = 3,

    /// <summary>Config push failed; see audit log for reason.</summary>
    Failed = 4
}
