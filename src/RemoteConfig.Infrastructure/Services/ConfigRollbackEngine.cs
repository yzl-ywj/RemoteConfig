using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;

namespace RemoteConfig.Infrastructure.Services;

/// <summary>
/// Rollback engine: diff two config versions and produce a human-readable delta.
/// </summary>
public class ConfigRollbackEngine : IConfigRollback
{
    private readonly IConfigRepository _configRepo;
    private readonly IRemoteConfigService _configService;
    private readonly ILogger<ConfigRollbackEngine> _logger;

    public ConfigRollbackEngine(
        IConfigRepository configRepo,
        IRemoteConfigService configService,
        ILogger<ConfigRollbackEngine> logger)
    {
        _configRepo = configRepo;
        _configService = configService;
        _logger = logger;
    }

    public async Task<DeviceConfig> RollbackAsync(
        string deviceId, int targetVersion, string reason, string caller, CancellationToken ct = default)
    {
        return await _configService.RollbackAsync(deviceId, targetVersion, reason, caller, ct);
    }

    public string GetDiff(DeviceConfig oldConfig, DeviceConfig newConfig)
    {
        var oldJson = oldConfig.Configuration.RootElement;
        var newJson = newConfig.Configuration.RootElement;

        var diffs = new List<string>();

        // Compare all properties in old config
        foreach (var prop in oldJson.EnumerateObject())
        {
            if (!newJson.TryGetProperty(prop.Name, out var newProp))
            {
                diffs.Add($"- {prop.Name}: {prop.Value} → [REMOVED]");
            }
            else if (prop.Value.GetRawText() != newProp.GetRawText())
            {
                diffs.Add($"~ {prop.Name}: {prop.Value} → {newProp}");
            }
        }

        // Check for new properties
        foreach (var prop in newJson.EnumerateObject())
        {
            if (!oldJson.TryGetProperty(prop.Name, out _))
            {
                diffs.Add($"+ {prop.Name}: [NEW] → {prop.Value}");
            }
        }

        return diffs.Count == 0
            ? "(no differences)"
            : string.Join(Environment.NewLine, diffs);
    }
}
