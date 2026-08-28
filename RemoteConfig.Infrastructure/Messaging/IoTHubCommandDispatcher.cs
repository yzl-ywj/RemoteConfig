using System.Text;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Infrastructure.Messaging;

/// <summary>
/// Sends commands directly to devices via Azure IoT Hub:
/// - Cloud-to-Device messages (async, fire-and-forget)
/// - Direct Methods (sync, request-response with timeout)
/// </summary>
public class IoTHubCommandDispatcher : ICommandDispatcher
{
    private readonly ServiceClient _serviceClient;
    private readonly RegistryManager _registryManager;
    private readonly IoTHubOptions _options;
    private readonly ICommandStore _commandStore;
    private readonly ILogger<IoTHubCommandDispatcher> _logger;

    public IoTHubCommandDispatcher(
        IOptions<IoTHubOptions> options,
        ICommandStore commandStore,
        ILogger<IoTHubCommandDispatcher> logger)
    {
        _options = options.Value;
        _commandStore = commandStore;
        _logger = logger;

        _serviceClient = ServiceClient.CreateFromConnectionString(
            _options.ConnectionString,
            TransportType.Amqp);

        _registryManager = RegistryManager.CreateFromConnectionString(_options.ConnectionString);
    }

    public async Task<CommandRequest> DispatchAsync(CommandRequest command, CancellationToken ct = default)
    {
        if (command.DeliveryMode == CommandDeliveryMode.DirectMethod)
        {
            await SendDirectMethodAsync(command, ct);
        }
        else
        {
            await SendCloudToDeviceAsync(command, ct);
        }

        command.Status = CommandStatus.Sent;
        command.SentAt = DateTime.UtcNow;
        await _commandStore.SaveAsync(command, ct);

        return command;
    }

    public async Task<IReadOnlyList<CommandRequest>> DispatchBatchAsync(
        IEnumerable<CommandRequest> commands, CancellationToken ct = default)
    {
        var list = commands.ToList();
        var tasks = list.Select(c => DispatchAsync(c, ct));
        await Task.WhenAll(tasks);
        return list.AsReadOnly();
    }

    public async Task<bool> CancelAsync(Guid commandId, CancellationToken ct = default)
    {
        // IoT Hub C2D messages can't be cancelled after send.
        // For Direct Methods, we just don't invoke them.
        var cmd = await _commandStore.GetByIdAsync(commandId, ct);
        if (cmd != null && cmd.Status == CommandStatus.Queued)
        {
            cmd.Status = CommandStatus.Cancelled;
            await _commandStore.SaveAsync(cmd, ct);
            return true;
        }
        return false;
    }

    public async Task UpdateStatusAsync(
        Guid commandId, CommandStatus status, string? resultMessage = null, CancellationToken ct = default)
    {
        var cmd = await _commandStore.GetByIdAsync(commandId, ct);
        if (cmd != null)
        {
            cmd.Status = status;
            if (status == CommandStatus.Acked) cmd.AckedAt = DateTime.UtcNow;
            cmd.ResultMessage = resultMessage ?? cmd.ResultMessage;
            await _commandStore.SaveAsync(cmd, ct);
        }
    }

    // ─── Private helpers ──────────────────────────────────

    private async Task SendCloudToDeviceAsync(CommandRequest command, CancellationToken ct)
    {
        var payload = command.Payload ?? "{}";
        var msg = new Message(Encoding.UTF8.GetBytes(payload))
        {
            MessageId = command.CommandId.ToString(),
            CorrelationId = command.CommandId.ToString(),
            ExpiryTimeUtc = DateTime.UtcNow.AddSeconds(command.TimeoutSeconds),
            Properties =
            {
                ["commandType"] = command.CommandType.ToString()
            }
        };

        await _serviceClient.SendAsync(command.DeviceId, msg);
        _logger.LogDebug("C2D message sent to {DeviceId}, cmd={Type}", command.DeviceId, command.CommandType);
    }

    private async Task SendDirectMethodAsync(CommandRequest command, CancellationToken ct)
    {
        var methodName = GetMethodName(command.CommandType);
        var payload = command.Payload ?? "{}";

        var method = new CloudToDeviceMethod(methodName, TimeSpan.FromSeconds(command.TimeoutSeconds))
        {
            ResponseTimeout = TimeSpan.FromSeconds(command.TimeoutSeconds)
        };
        method.SetPayloadJson(payload);

        var response = await _serviceClient.InvokeDeviceMethodAsync(command.DeviceId, method);
        
        if (response.Status >= 200 && response.Status < 300)
        {
            command.Status = CommandStatus.Acked;
            command.AckedAt = DateTime.UtcNow;
            command.ResultMessage = response.GetPayloadAsJson();
            _logger.LogInformation("Direct method {Method} succeeded on {DeviceId}: {Status}",
                methodName, command.DeviceId, response.Status);
        }
        else
        {
            command.Status = CommandStatus.Failed;
            command.ErrorMessage = $"Device returned status {response.Status}: {response.GetPayloadAsJson()}";
            _logger.LogWarning("Direct method {Method} failed on {DeviceId}: {Error}",
                methodName, command.DeviceId, command.ErrorMessage);
        }
    }

    private static string GetMethodName(CommandType type) => type switch
    {
        CommandType.Reboot => "reboot",
        CommandType.RestartApp => "restartApp",
        CommandType.ToggleGpio => "toggleGpio",
        CommandType.CheckFirmware => "checkFirmware",
        CommandType.FactoryReset => "factoryReset",
        CommandType.CollectLogs => "collectLogs",
        _ => "custom"
    };
}
