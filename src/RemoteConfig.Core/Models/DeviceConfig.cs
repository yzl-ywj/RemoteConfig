using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using RemoteConfig.Core.Enums;

namespace RemoteConfig.Core.Models;

/// <summary>
/// Represents the current desired configuration for a device.
/// Stored as a JSON document with version history.
/// </summary>
public class DeviceConfig
{
    /// <summary>Unique config record ID.</summary>
    public Guid ConfigId { get; set; } = Guid.NewGuid();

    /// <summary>Device this config belongs to.</summary>
    [Required]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Sequential version number (1, 2, 3...).</summary>
    public int Version { get; set; } = 1;

    /// <summary>The actual configuration payload (JSON).</summary>
    [Required]
    public JsonDocument Configuration { get; set; } = JsonDocument.Parse("{}");

    /// <summary>Human-readable description of this config version.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Who/what created this config version.</summary>
    [MaxLength(256)]
    public string ChangedBy { get; set; } = "system";

    /// <summary>Lifecycle status of this config version.</summary>
    public ConfigStatus Status { get; set; } = ConfigStatus.Active;

    /// <summary>Previous version number (null if first version).</summary>
    public int? PreviousVersion { get; set; }

    /// <summary>Reason for rollback (if Status == RolledBack).</summary>
    [MaxLength(500)]
    public string? RollbackReason { get; set; }

    /// <summary>When this config was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this config was last modified.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Tags for grouping/filtering configs.</summary>
    public Dictionary<string, string> Tags { get; set; } = new();
}
