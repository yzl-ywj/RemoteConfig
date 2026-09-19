namespace RemoteConfig.Infrastructure.Configuration;

/// <summary>
/// Azure Service Bus options for command queue.
/// </summary>
public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>Service Bus namespace connection string.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Queue name for outgoing commands.</summary>
    public string CommandQueueName { get; set; } = "device-commands";

    /// <summary>Maximum delivery attempts before dead-letter.</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>Lock duration in seconds.</summary>
    public int LockDurationSeconds { get; set; } = 30;
}
