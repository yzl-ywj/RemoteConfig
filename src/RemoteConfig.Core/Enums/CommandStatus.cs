namespace RemoteConfig.Core.Enums;

/// <summary>
/// Delivery status of a command sent to a device.
/// </summary>
public enum CommandStatus
{
    /// <summary>Command is queued, not yet sent to IoT Hub.</summary>
    Queued = 0,

    /// <summary>Command was sent to IoT Hub / Service Bus.</summary>
    Sent = 1,

    /// <summary>Device acknowledged / executed successfully.</summary>
    Acked = 2,

    /// <summary>Device reported an error executing the command.</summary>
    Failed = 3,

    /// <summary>No response within the timeout window.</summary>
    Timeout = 4,

    /// <summary>Command was cancelled before delivery.</summary>
    Cancelled = 5,

    /// <summary>Command moved to dead-letter queue after max retries.</summary>
    DeadLettered = 6
}
