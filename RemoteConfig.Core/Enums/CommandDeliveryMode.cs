namespace RemoteConfig.Core.Enums;

/// <summary>
/// How a command is delivered to the device.
/// </summary>
public enum CommandDeliveryMode
{
    /// <summary>Cloud-to-Device message (asynchronous, at-least-once).</summary>
    CloudToDevice = 0,

    /// <summary>Direct Method invocation (synchronous, request-response).</summary>
    DirectMethod = 1
}
