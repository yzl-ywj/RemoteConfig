namespace RemoteConfig.Infrastructure.Configuration;

/// <summary>
/// Azure IoT Hub options for command delivery.
/// </summary>
public class IoTHubOptions
{
    public const string SectionName = "IoTHub";

    /// <summary>IoT Hub host name (e.g., myhub.azure-devices.net).</summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>Connection string for IoT Hub service client.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Default TTL for C2D messages (seconds).</summary>
    public int DefaultMessageTtlSeconds { get; set; } = 60;

    /// <summary>Direct method timeout (seconds).</summary>
    public int DirectMethodTimeoutSeconds { get; set; } = 30;
}
