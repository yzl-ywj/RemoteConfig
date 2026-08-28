using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Api.Controllers;

/// <summary>
/// REST API for device configuration management.
/// Base path: /api/v1/configs
/// </summary>
[ApiController]
[Route("api/v1/configs")]
[Authorize]
public class ConfigsController : ControllerBase
{
    private readonly IRemoteConfigService _configService;
    private readonly IConfigRollback _rollbackEngine;
    private readonly ICommandStore _commandStore;
    private readonly IConfigRepository _configRepository;
    private readonly ILogger<ConfigsController> _logger;
    private readonly RemoteConfigOptions _options;

    public ConfigsController(
        IRemoteConfigService configService,
        IConfigRollback rollbackEngine,
        ICommandStore commandStore,
        IConfigRepository configRepository,
        IOptions<RemoteConfigOptions> options,
        ILogger<ConfigsController> logger)
    {
        _configService = configService;
        _rollbackEngine = rollbackEngine;
        _commandStore = commandStore;
        _configRepository = configRepository;
        _options = options.Value;
        _logger = logger;
    }

    // ─── GET /api/v1/configs/{deviceId} ────────────────
    /// <summary>Get the active configuration for a device.</summary>
    [HttpGet("{deviceId}")]
    [Authorize(Policy = "Config.Read")]
    [ProducesResponseType(typeof(DeviceConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceConfig>> GetActive(string deviceId, CancellationToken ct)
    {
        var config = await _configService.GetActiveConfigAsync(deviceId, ct);
        if (config == null) return NotFound(new { error = $"No config found for device '{deviceId}'" });
        return Ok(config);
    }

    // ─── PUT /api/v1/configs/{deviceId} ────────────────
    /// <summary>Push a new configuration to a device. Creates a new version.</summary>
    [HttpPut("{deviceId}")]
    [Authorize(Policy = "Config.Write")]
    [ProducesResponseType(typeof(DeviceConfig), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DeviceConfig>> PushConfig(
        string deviceId,
        [FromBody] PushConfigRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Parse JSON configuration
        JsonDocument configDoc;
        try
        {
            configDoc = JsonDocument.Parse(request.Configuration);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
        }

        // Validate size
        var rawSize = System.Text.Encoding.UTF8.GetByteCount(configDoc.RootElement.GetRawText());
        if (rawSize > _options.MaxConfigSizeBytes)
        {
            return BadRequest(new
            {
                error = $"Config too large: {rawSize} bytes (max {_options.MaxConfigSizeBytes})"
            });
        }

        var caller = User.Identity?.Name ?? "system";
        var config = await _configService.PushConfigAsync(
            deviceId, configDoc, caller, request.Description, request.Tags, ct);

        _logger.LogInformation("Config v{Version} pushed to {DeviceId} by {Caller}",
            config.Version, deviceId, caller);

        return CreatedAtAction(nameof(GetActive), new { deviceId }, config);
    }

    // ─── GET /api/v1/configs/{deviceId}/versions ───────
    /// <summary>List all config versions for a device.</summary>
    [HttpGet("{deviceId}/versions")]
    [Authorize(Policy = "Config.Read")]
    [ProducesResponseType(typeof(IReadOnlyList<ConfigVersionInfo>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ConfigVersionInfo>>> GetVersions(
        string deviceId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        if (take > 100) take = 100;
        var versions = await _configService.GetVersionHistoryAsync(deviceId, skip, take, ct);
        return Ok(versions);
    }

    // ─── POST /api/v1/configs/{deviceId}/rollback/{versionId} ──
    /// <summary>Rollback device to a specific config version.</summary>
    [HttpPost("{deviceId}/rollback/{versionId:int}")]
    [Authorize(Policy = "Config.Write")]
    [ProducesResponseType(typeof(DeviceConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DeviceConfig>> Rollback(
        string deviceId,
        int versionId,
        [FromBody] RollbackRequest? request,
        CancellationToken ct)
    {
        var reason = request?.Reason ?? "Manual rollback via API";
        var caller = User.Identity?.Name ?? "system";

        try
        {
            var config = await _configService.RollbackAsync(deviceId, versionId, reason, caller, ct);
            return Ok(config);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // ─── GET /api/v1/configs/{deviceId}/diff ───────────
    /// <summary>Get diff between two config versions.</summary>
    [HttpGet("{deviceId}/diff")]
    [Authorize(Policy = "Config.Read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetDiff(
        string deviceId,
        [FromQuery] int fromVersion,
        [FromQuery] int toVersion,
        CancellationToken ct)
    {
        var versions = await _configService.GetVersionHistoryAsync(deviceId, 0, 100, ct);
        var from = versions.FirstOrDefault(v => v.Version == fromVersion);
        var to = versions.FirstOrDefault(v => v.Version == toVersion);

        if (from == null || to == null)
            return NotFound(new { error = "One or both versions not found" });

        // Get full configs
        var fromFull = await GetFullConfigAsync(deviceId, from.ConfigId, ct);
        var toFull = await GetFullConfigAsync(deviceId, to.ConfigId, ct);

        if (fromFull == null || toFull == null)
            return NotFound(new { error = "Config details not found" });

        var diff = _rollbackEngine.GetDiff(fromFull, toFull);
        return Ok(new { fromVersion, toVersion, diff });
    }

    // ─── POST /api/v1/configs/batch ─────────────────────
    /// <summary>Push config to multiple devices (batch operation).</summary>
    [HttpPost("batch")]
    [Authorize(Policy = "Config.Write")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<ActionResult> BatchPush(
        [FromBody] BatchPushRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (request.DeviceIds.Count > 1000)
            return BadRequest(new { error = "Maximum 1000 devices per batch" });

        // Enqueue as background work (fire and forget with Task.Run)
        var caller = User.Identity?.Name ?? "system";
        JsonDocument configDoc;
        try
        {
            configDoc = JsonDocument.Parse(request.Configuration);
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
        }

        // Process in background
        _ = Task.Run(async () =>
        {
            var delay = Math.Max(1, 1000 / _options.PerDeviceRateLimit);
            foreach (var deviceId in request.DeviceIds)
            {
                try
                {
                    await _configService.PushConfigAsync(
                        deviceId, configDoc, caller, request.Description, request.Tags, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch push failed for {DeviceId}", deviceId);
                }
                await Task.Delay(delay, ct);
            }
        }, ct);

        return Accepted(new
        {
            message = $"Batch push initiated for {request.DeviceIds.Count} devices",
            rateLimitPerSec = _options.PerDeviceRateLimit
        });
    }

    // ─── Helper ──────────────────────────────────────────
    private async Task<DeviceConfig?> GetFullConfigAsync(
        string deviceId, Guid configId, CancellationToken ct)
    {
        return await _configRepository.GetByIdAsync(configId, ct);
    }
}

// ────────────────────────────────────────────────────────
// Request DTOs
// ────────────────────────────────────────────────────────

public class PushConfigRequest
{
    [Required]
    public string Configuration { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public Dictionary<string, string>? Tags { get; set; }
}

public class RollbackRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = "Rollback requested";
}

public class BatchPushRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(1000)]
    public List<string> DeviceIds { get; set; } = new();

    [Required]
    public string Configuration { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public Dictionary<string, string>? Tags { get; set; }
}
