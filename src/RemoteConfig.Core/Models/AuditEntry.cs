using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RemoteConfig.Core.Models;

/// <summary>
/// Tamper-proof audit log entry for config changes and commands.
/// </summary>
public class AuditEntry
{
    /// <summary>Audit record ID.</summary>
    public Guid AuditId { get; set; } = Guid.NewGuid();

    /// <summary>Device affected by this action.</summary>
    [Required]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Action type (CONFIG_CREATE, CONFIG_ROLLBACK, COMMAND_SENT, etc.).</summary>
    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Who performed the action.</summary>
    [Required]
    [MaxLength(256)]
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>Old values (JSON snapshot).</summary>
    public JsonDocument? OldValues { get; set; }

    /// <summary>New values (JSON snapshot).</summary>
    public JsonDocument? NewValues { get; set; }

    /// <summary>Additional context (reason, error message, etc.).</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>When the audit entry was created (UTC).</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Correlation ID for tracing across services.</summary>
    public string? CorrelationId { get; set; }
}
