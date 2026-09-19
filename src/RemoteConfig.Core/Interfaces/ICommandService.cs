using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Models;

namespace RemoteConfig.Core.Interfaces;

/// <summary>
/// High-level service interface for command execution.
/// </summary>
public interface ICommandService
{
    /// <summary>Send a command to a device.</summary>
    Task<CommandRequest> SendCommandAsync(
        string deviceId,
        CommandType commandType,
        string caller,
        string? payload = null,
        CommandDeliveryMode mode = CommandDeliveryMode.CloudToDevice,
        int timeoutSeconds = 30,
        CancellationToken ct = default);

    /// <summary>Send a batch command to multiple devices.</summary>
    Task<IReadOnlyList<CommandRequest>> SendBatchCommandAsync(
        IEnumerable<string> deviceIds,
        CommandType commandType,
        string caller,
        string? payload = null,
        int rateLimitPerSec = 10,
        CancellationToken ct = default);

    /// <summary>Get command status.</summary>
    Task<CommandResult?> GetCommandStatusAsync(Guid commandId, CancellationToken ct = default);

    /// <summary>Cancel a pending command.</summary>
    Task<bool> CancelCommandAsync(Guid commandId, string caller, CancellationToken ct = default);

    /// <summary>Search commands with filters.</summary>
    Task<IReadOnlyList<CommandRequest>> SearchCommandsAsync(
        string? deviceId = null,
        CommandStatus? status = null,
        DateTime? from = null,
        DateTime? target = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);
}
