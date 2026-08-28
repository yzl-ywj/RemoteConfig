namespace RemoteConfig.Infrastructure.Configuration;

/// <summary>
/// Database connection options.
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Azure SQL connection string (Managed Identity auth).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Command timeout in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
