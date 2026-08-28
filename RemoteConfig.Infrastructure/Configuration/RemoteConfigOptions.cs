namespace RemoteConfig.Infrastructure.Configuration;

/// <summary>
/// Configuration for the Remote Config service.
/// </summary>
public class RemoteConfigOptions
{
    public const string SectionName = "RemoteConfig";

    /// <summary>Maximum config payload size in bytes (default 64KB).</summary>
    public int MaxConfigSizeBytes { get; set; } = 65536;

    /// <summary>Maximum versions to keep per device (oldest pruned).</summary>
    public int MaxVersionsPerDevice { get; set; } = 50;

    /// <summary>Default command timeout in seconds.</summary>
    public int DefaultCommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Rate limit: commands per second per device.</summary>
    public int PerDeviceRateLimit { get; set; } = 10;

    /// <summary>Rate limit: commands per second per tenant.</summary>
    public int PerTenantRateLimit { get; set; } = 100;

    /// <summary>Redis key prefix for pending commands.</summary>
    public string RedisKeyPrefix { get; set; } = "rc:cmd:";

    /// <summary>Service Bus queue name for commands.</summary>
    public string ServiceBusQueueName { get; set; } = "device-commands";
}
