using RemoteConfig.Core.Enums;

namespace RemoteConfig.Core.Models;

/// <summary>
/// Lightweight config version summary for listing endpoints.
/// </summary>
public class ConfigVersionInfo
{
    public Guid ConfigId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public int Version { get; set; }
    public ConfigStatus Status { get; set; }
    public string? Description { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? PreviousVersion { get; set; }
}
