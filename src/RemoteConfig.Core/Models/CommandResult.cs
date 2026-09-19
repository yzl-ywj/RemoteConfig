using RemoteConfig.Core.Enums;

namespace RemoteConfig.Core.Models;

/// <summary>
/// Result of a command execution returned to the API caller.
/// </summary>
public class CommandResult
{
    /// <summary>The command ID.</summary>
    public Guid CommandId { get; set; }

    /// <summary>Target device ID.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Command type.</summary>
    public CommandType CommandType { get; set; }

    /// <summary>Current status.</summary>
    public CommandStatus Status { get; set; }

    /// <summary>Result message from device.</summary>
    public string? ResultMessage { get; set; }

    /// <summary>Error details if failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>When the command was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the command was acknowledged.</summary>
    public DateTime? AckedAt { get; set; }

    /// <summary>Number of retries attempted.</summary>
    public int RetryCount { get; set; }
}
