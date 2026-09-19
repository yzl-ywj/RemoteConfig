using RemoteConfig.Core.Models;

namespace RemoteConfig.Core.Interfaces;

/// <summary>
/// Append-only audit logger for config changes and commands.
/// </summary>
public interface IAuditLogger
{
    /// <summary>Write an audit entry (append-only, never updated/deleted).</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>Read audit history for a device (newest first).</summary>
    Task<IReadOnlyList<AuditEntry>> GetHistoryAsync(string deviceId, int limit = 100, CancellationToken ct = default);

    /// <summary>Read audit history for a specific config version.</summary>
    Task<IReadOnlyList<AuditEntry>> GetByConfigIdAsync(Guid configId, CancellationToken ct = default);
}
