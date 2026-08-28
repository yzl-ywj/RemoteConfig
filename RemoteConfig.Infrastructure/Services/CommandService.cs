using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Infrastructure.Services;

/// <summary>
/// Business logic for command execution.
/// Handles single + batch commands with rate limiting and audit.
/// </summary>
public class CommandService : ICommandService
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ICommandStore _commandStore;
    private readonly IAuditLogger _auditLogger;
    private readonly RemoteConfigOptions _options;
    private readonly ILogger<CommandService> _logger;
    private readonly SemaphoreSlim _rateLimiter;

    public CommandService(
        ICommandDispatcher dispatcher,
        ICommandStore commandStore,
        IAuditLogger auditLogger,
        IOptions<RemoteConfigOptions> options,
        ILogger<CommandService> logger)
    {
        _dispatcher = dispatcher;
        _commandStore = commandStore;
        _auditLogger = auditLogger;
        _options = options.Value;
        _logger = logger;

        // Rate limiter: per-tenant limit
        _rateLimiter = new SemaphoreSlim(_options.PerTenantRateLimit, _options.PerTenantRateLimit);
    }

    public async Task<CommandRequest> SendCommandAsync(
        string deviceId,
        CommandType commandType,
        string caller,
        string? payload = null,
        CommandDeliveryMode mode = CommandDeliveryMode.CloudToDevice,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        // Rate limit check
        await _rateLimiter.WaitAsync(TimeSpan.FromSeconds(5), ct);

        try
        {
            var command = new CommandRequest
            {
                CommandId = Guid.NewGuid(),
                DeviceId = deviceId,
                CommandType = commandType,
                Payload = payload,
                DeliveryMode = mode,
                TimeoutSeconds = timeoutSeconds,
                Status = CommandStatus.Queued,
                IssuedBy = caller
            };

            // Save to store first
            await _commandStore.SaveAsync(command, ct);

            // Dispatch
            var result = await _dispatcher.DispatchAsync(command, ct);

            // Audit
            await _auditLogger.WriteAsync(new AuditEntry
            {
                DeviceId = deviceId,
                Action = "COMMAND_SENT",
                ChangedBy = caller,
                Metadata = new Dictionary<string, string>
                {
                    ["commandId"] = result.CommandId.ToString(),
                    ["commandType"] = commandType.ToString(),
                    ["deliveryMode"] = mode.ToString()
                }
            }, ct);

            _logger.LogInformation(
                "Command {CommandId} ({Type}) sent to {DeviceId} by {Caller}",
                result.CommandId, commandType, deviceId, caller);

            return result;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    public async Task<IReadOnlyList<CommandRequest>> SendBatchCommandAsync(
        IEnumerable<string> deviceIds,
        CommandType commandType,
        string caller,
        string? payload = null,
        int rateLimitPerSec = 10,
        CancellationToken ct = default)
    {
        var ids = deviceIds.ToList();
        var results = new List<CommandRequest>();
        var delayPerCommand = Math.Max(1, 1000 / rateLimitPerSec); // ms between commands

        _logger.LogInformation(
            "Batch command {Type} starting for {Count} devices (rate={Rate}/s)",
            commandType, ids.Count, rateLimitPerSec);

        foreach (var deviceId in ids)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var cmd = await SendCommandAsync(
                    deviceId, commandType, caller, payload,
                    CommandDeliveryMode.CloudToDevice,
                    _options.DefaultCommandTimeoutSeconds,
                    ct);
                results.Add(cmd);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch command failed for device {DeviceId}", deviceId);
                // Continue with remaining devices
            }

            if (delayPerCommand > 0)
                await Task.Delay(delayPerCommand, ct);
        }

        _logger.LogInformation(
            "Batch command completed: {Success}/{Total} succeeded",
            results.Count, ids.Count);

        return results.AsReadOnly();
    }

    public async Task<CommandResult?> GetCommandStatusAsync(Guid commandId, CancellationToken ct = default)
    {
        var cmd = await _commandStore.GetByIdAsync(commandId, ct);
        if (cmd == null) return null;

        return new CommandResult
        {
            CommandId = cmd.CommandId,
            DeviceId = cmd.DeviceId ?? string.Empty,
            CommandType = cmd.CommandType,
            Status = cmd.Status,
            ResultMessage = cmd.ResultMessage,
            ErrorMessage = cmd.ErrorMessage,
            CreatedAt = cmd.CreatedAt,
            AckedAt = cmd.AckedAt,
            RetryCount = cmd.RetryCount
        };
    }

    public async Task<bool> CancelCommandAsync(Guid commandId, string caller, CancellationToken ct = default)
    {
        var cmd = await _commandStore.GetByIdAsync(commandId, ct);
        if (cmd == null) return false;

        var cancelled = await _dispatcher.CancelAsync(commandId, ct);

        if (cancelled)
        {
            await _auditLogger.WriteAsync(new AuditEntry
            {
                DeviceId = cmd.DeviceId ?? "unknown",
                Action = "COMMAND_CANCELLED",
                ChangedBy = caller,
                Metadata = new Dictionary<string, string>
                {
                    ["commandId"] = commandId.ToString()
                }
            }, ct);
        }

        return cancelled;
    }

    public async Task<IReadOnlyList<CommandRequest>> SearchCommandsAsync(
        string? deviceId = null,
        CommandStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        // The SQL store has the full search implementation.
        // We access it via the ICommandStore interface.
        if (_commandStore is ICommandStore store)
        {
            return await store.SearchAsync(deviceId, status, from, to, skip, take, ct);
        }

        // Fallback: return empty if store doesn't support search
        return new List<CommandRequest>().AsReadOnly();
    }
}
