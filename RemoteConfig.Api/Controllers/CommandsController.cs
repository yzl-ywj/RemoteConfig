using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using System.ComponentModel.DataAnnotations;

namespace RemoteConfig.Api.Controllers;

/// <summary>
/// REST API for command execution on devices.
/// Base path: /api/v1/commands
/// </summary>
[ApiController]
[Route("api/v1/commands")]
[Authorize]
public class CommandsController : ControllerBase
{
    private readonly ICommandService _commandService;
    private readonly ILogger<CommandsController> _logger;

    public CommandsController(
        ICommandService commandService,
        ILogger<CommandsController> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    // ─── POST /api/v1/commands ──────────────────────────
    /// <summary>Send a command to a device.</summary>
    [HttpPost]
    [Authorize(Policy = "Command.Execute")]
    [ProducesResponseType(typeof(CommandRequest), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommandRequest>> SendCommand(
        [FromBody] SendCommandRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var caller = User.Identity?.Name ?? "system";

        try
        {
            var result = await _commandService.SendCommandAsync(
                request.DeviceId,
                request.CommandType,
                caller,
                request.Payload,
                request.DeliveryMode,
                request.TimeoutSeconds,
                ct);

            _logger.LogInformation(
                "Command {CommandId} ({Type}) sent to {DeviceId} by {Caller}",
                result.CommandId, request.CommandType, request.DeviceId, caller);

            return Accepted(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to {DeviceId}", request.DeviceId);
            return BadRequest(new { error = ex.Message });
        }
    }

    // ─── GET /api/v1/commands/{commandId} ───────────────
    /// <summary>Get the status of a command.</summary>
    [HttpGet("{commandId}")]
    [Authorize(Policy = "Command.Read")]
    [ProducesResponseType(typeof(CommandResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommandResult>> GetCommand(Guid commandId, CancellationToken ct)
    {
        var result = await _commandService.GetCommandStatusAsync(commandId, ct);
        if (result == null) return NotFound(new { error = $"Command '{commandId}' not found" });
        return Ok(result);
    }

    // ─── GET /api/v1/commands ───────────────────────────
    /// <summary>List commands with optional filters.</summary>
    [HttpGet]
    [Authorize(Policy = "Command.Read")]
    [ProducesResponseType(typeof(IReadOnlyList<CommandRequest>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CommandRequest>>> ListCommands(
        [FromQuery] string? deviceId,
        [FromQuery] CommandStatus? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (take > 500) take = 500;

        // Delegate to the store via the service layer
        var results = await _commandService.SearchCommandsAsync(
            deviceId, status, from, to, skip, take, ct);
        return Ok(results);
    }

    // ─── DELETE /api/v1/commands/{commandId} ────────────
    /// <summary>Cancel a pending command.</summary>
    [HttpDelete("{commandId}")]
    [Authorize(Policy = "Command.Execute")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> CancelCommand(Guid commandId, CancellationToken ct)
    {
        var caller = User.Identity?.Name ?? "system";
        var cancelled = await _commandService.CancelCommandAsync(commandId, caller, ct);

        if (!cancelled) return NotFound(new { error = $"Command '{commandId}' not found or cannot be cancelled" });
        return NoContent();
    }

    // ─── POST /api/v1/commands/batch ────────────────────
    /// <summary>Send a batch command to multiple devices.</summary>
    [HttpPost("batch")]
    [Authorize(Policy = "Command.Execute")]
    [ProducesResponseType(typeof(IReadOnlyList<CommandRequest>), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<IReadOnlyList<CommandRequest>>> BatchCommand(
        [FromBody] BatchCommandRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (request.DeviceIds.Count > 1000)
            return BadRequest(new { error = "Maximum 1000 devices per batch" });

        var caller = User.Identity?.Name ?? "system";

        var results = await _commandService.SendBatchCommandAsync(
            request.DeviceIds,
            request.CommandType,
            caller,
            request.Payload,
            request.RateLimitPerSec,
            ct);

        return Accepted(results);
    }
}

// ────────────────────────────────────────────────────────
// Request DTOs
// ────────────────────────────────────────────────────────

public class SendCommandRequest
{
    [Required]
    [MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    public CommandType CommandType { get; set; }

    public string? Payload { get; set; }

    public CommandDeliveryMode DeliveryMode { get; set; } = CommandDeliveryMode.CloudToDevice;

    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}

public class BatchCommandRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(1000)]
    public List<string> DeviceIds { get; set; } = new();

    [Required]
    public CommandType CommandType { get; set; }

    public string? Payload { get; set; }

    [Range(1, 100)]
    public int RateLimitPerSec { get; set; } = 10;
}
