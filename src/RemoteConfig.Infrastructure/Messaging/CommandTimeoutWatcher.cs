using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Infrastructure.Messaging;

/// <summary>
/// BackgroundService that periodically checks for commands that have exceeded
/// their timeout window without receiving an ACK. Marks them as TimedOut.
/// </summary>
public class CommandTimeoutWatcher : BackgroundService
{
    private readonly ICommandStore _commandStore;
    private readonly IAuditLogger _auditLogger;
    private readonly RemoteConfigOptions _options;
    private readonly ILogger<CommandTimeoutWatcher> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public CommandTimeoutWatcher(
        ICommandStore commandStore,
        IAuditLogger auditLogger,
        IOptions<RemoteConfigOptions> options,
        ILogger<CommandTimeoutWatcher> logger)
    {
        _commandStore = commandStore;
        _auditLogger = auditLogger;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CommandTimeoutWatcher started (interval={Interval}s)",
            _checkInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckTimeoutsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CommandTimeoutWatcher cycle");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckTimeoutsAsync(CancellationToken ct)
    {
        // Get all sent commands older than their timeout
        var cutoff = DateTime.UtcNow.AddMinutes(-5); // Look back 5 min
        var pending = await _commandStore.SearchAsync(
            status: CommandStatus.Sent,
            from: cutoff.AddHours(-1),
            target: cutoff,
            take: 200,
            ct: ct);

        var timeoutCount = 0;
        foreach (var cmd in pending)
        {
            if (cmd.SentAt == null) continue;

            var elapsed = DateTime.UtcNow - cmd.SentAt.Value;
            if (elapsed.TotalSeconds > cmd.TimeoutSeconds)
            {
                // Mark as timed out
                cmd.Status = CommandStatus.Timeout;
                cmd.ErrorMessage = $"Command timed out after {elapsed.TotalSeconds:F0}s (limit: {cmd.TimeoutSeconds}s)";
                await _commandStore.SaveAsync(cmd, ct);

                await _auditLogger.WriteAsync(new AuditEntry
                {
                    DeviceId = cmd.DeviceId ?? "unknown",
                    Action = "COMMAND_TIMEOUT",
                    ChangedBy = "system",
                    Metadata = new Dictionary<string, string>
                    {
                        ["commandId"] = cmd.CommandId.ToString(),
                        ["elapsedSeconds"] = ((int)elapsed.TotalSeconds).ToString(),
                        ["timeoutSeconds"] = cmd.TimeoutSeconds.ToString()
                    }
                }, ct);

                timeoutCount++;
            }
        }

        if (timeoutCount > 0)
            _logger.LogWarning("Marked {Count} commands as timed out", timeoutCount);
    }
}
