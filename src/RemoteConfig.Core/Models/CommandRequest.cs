using System.ComponentModel.DataAnnotations;
using RemoteConfig.Core.Enums;

namespace RemoteConfig.Core.Models;

/// <summary>
/// A command request sent from the cloud to one or more devices.
/// </summary>
public class CommandRequest
{
    /// <summary>Unique command ID (client-generated for idempotency).</summary>
    public Guid CommandId { get; set; } = Guid.NewGuid();

    /// <summary>Target device ID (null for batch commands).</summary>
    public string? DeviceId { get; set; }

    /// <summary>Target device group ID (for batch commands).</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Type of command to execute.</summary>
    [Required]
    public CommandType CommandType { get; set; }

    /// <summary>Optional payload (JSON) for the command.</summary>
    public string? Payload { get; set; }

    /// <summary>How the command is delivered.</summary>
    public CommandDeliveryMode DeliveryMode { get; set; } = CommandDeliveryMode.CloudToDevice;

    /// <summary>Timeout in seconds for command execution.</summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum retry attempts before dead-letter.</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Current status of the command.</summary>
    public CommandStatus Status { get; set; } = CommandStatus.Queued;

    /// <summary>Result message from the device (if any).</summary>
    public string? ResultMessage { get; set; }

    /// <summary>Who issued the command.</summary>
    public string IssuedBy { get; set; } = "system";

    /// <summary>When the command was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the command was sent to the device.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>When the device acknowledged the command.</summary>
    public DateTime? AckedAt { get; set; }

    /// <summary>Number of retry attempts so far.</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>Error message if command failed.</summary>
    public string? ErrorMessage { get; set; }
}
