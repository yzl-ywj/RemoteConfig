using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Models;

namespace RemoteConfig.Core.Interfaces;

/// <summary>
/// Store for tracking pending commands (Redis-backed in production).
/// </summary>
public interface ICommandStore
{
    /// <summary>Save a pending command.</summary>
    Task SaveAsync(CommandRequest command, CancellationToken ct = default);

    /// <summary>Get a command by ID.</summary>
    Task<CommandRequest?> GetByIdAsync(Guid commandId, CancellationToken ct = default);

    /// <summary>Get all pending commands for a device.</summary>
    Task<IReadOnlyList<CommandRequest>> GetPendingForDeviceAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Remove a command (after ACK or timeout).</summary>
    Task RemoveAsync(Guid commandId, CancellationToken ct = default);

    /// <summary>List all commands with filters.</summary>
    Task<IReadOnlyList<CommandRequest>> SearchAsync(
        string? deviceId = null,
        CommandStatus? status = null,
        DateTime? from = null,
        DateTime? target = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);
}
