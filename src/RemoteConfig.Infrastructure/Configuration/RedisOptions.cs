namespace RemoteConfig.Infrastructure.Configuration;

/// <summary>
/// Redis connection options for pending command cache.
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>Redis connection string.</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>Default TTL for pending commands (seconds).</summary>
    public int DefaultTtlSeconds { get; set; } = 300;

    /// <summary>Instance name for key prefixing.</summary>
    public string InstanceName { get; set; } = "iot-rc";

    public string RedisKeyPrefix { get; set; } = "cmd:";
}
