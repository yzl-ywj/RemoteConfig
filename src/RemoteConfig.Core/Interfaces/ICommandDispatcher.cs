using RemoteConfig.Core.Models;
using RemoteConfig.Core.Enums;

namespace RemoteConfig.Core.Interfaces;

/// <summary>
/// Dispatches commands to devices via IoT Hub / Service Bus.
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>Send a command to a single device. Returns updated command request.</summary>
    Task<CommandRequest> DispatchAsync(CommandRequest command, CancellationToken ct = default);

    /// <summary>Send commands to multiple devices (batch). Returns list of command requests.</summary>
    Task<IReadOnlyList<CommandRequest>> DispatchBatchAsync(IEnumerable<CommandRequest> commands, CancellationToken ct = default);

    /// <summary>Cancel a pending command (if not yet sent).</summary>
    Task<bool> CancelAsync(Guid commandId, CancellationToken ct = default);

    /// <summary>Update command status (called by result processor).</summary>
    Task UpdateStatusAsync(Guid commandId, CommandStatus status, string? resultMessage = null, CancellationToken ct = default);
}
