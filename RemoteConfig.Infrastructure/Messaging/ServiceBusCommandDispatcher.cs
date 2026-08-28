using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Infrastructure.Messaging;

/// <summary>
/// Dispatches commands via Azure Service Bus queue.
/// IoT Hub routing is handled by a separate consumer of this queue.
/// </summary>
public class ServiceBusCommandDispatcher : ICommandDispatcher
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusOptions _options;
    private readonly ICommandStore _commandStore;
    private readonly ILogger<ServiceBusCommandDispatcher> _logger;

    public ServiceBusCommandDispatcher(
        IOptions<ServiceBusOptions> options,
        ICommandStore commandStore,
        ILogger<ServiceBusCommandDispatcher> logger)
    {
        _options = options.Value;
        _commandStore = commandStore;
        _logger = logger;

        _client = new ServiceBusClient(_options.ConnectionString);
        _sender = _client.CreateSender(_options.CommandQueueName);
    }

    public async Task<CommandRequest> DispatchAsync(CommandRequest command, CancellationToken ct = default)
    {
        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(command))
        {
            MessageId = command.CommandId.ToString(),
            CorrelationId = command.CommandId.ToString(),
            Subject = $"command:{command.CommandType}",
            TimeToLive = TimeSpan.FromSeconds(command.TimeoutSeconds * 2),
            ApplicationProperties =
            {
                ["deviceId"] = command.DeviceId ?? "",
                ["commandType"] = command.CommandType.ToString(),
                ["deliveryMode"] = ((int)command.DeliveryMode).ToString()
            }
        };

        await _sender.SendMessageAsync(message, ct);
        command.Status = CommandStatus.Sent;
        command.SentAt = DateTime.UtcNow;

        await _commandStore.SaveAsync(command, ct);

        _logger.LogInformation(
            "Command {CommandId} dispatched to Service Bus for device {DeviceId}, type={Type}",
            command.CommandId, command.DeviceId, command.CommandType);

        return command;
    }

    public async Task<IReadOnlyList<CommandRequest>> DispatchBatchAsync(
        IEnumerable<CommandRequest> commands, CancellationToken ct = default)
    {
        var list = commands.ToList();
        var messages = list.Select(c => new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(c))
        {
            MessageId = c.CommandId.ToString(),
            CorrelationId = c.CommandId.ToString(),
            Subject = $"command:{c.CommandType}",
            ApplicationProperties =
            {
                ["deviceId"] = c.DeviceId ?? "",
                ["commandType"] = c.CommandType.ToString()
            }
        }).ToList();

        await _sender.SendMessagesAsync(messages, ct);

        foreach (var cmd in list)
        {
            cmd.Status = CommandStatus.Sent;
            cmd.SentAt = DateTime.UtcNow;
            await _commandStore.SaveAsync(cmd, ct);
        }

        _logger.LogInformation("Batch dispatched {Count} commands to Service Bus", list.Count);
        return list.AsReadOnly();
    }

    public async Task<bool> CancelAsync(Guid commandId, CancellationToken ct = default)
    {
        var cmd = await _commandStore.GetByIdAsync(commandId, ct);
        if (cmd == null || (cmd.Status != CommandStatus.Queued && cmd.Status != CommandStatus.Sent))
            return false;

        await _commandStore.RemoveAsync(commandId, ct);
        _logger.LogInformation("Command {CommandId} cancelled", commandId);
        return true;
    }

    public async Task UpdateStatusAsync(
        Guid commandId, CommandStatus status, string? resultMessage = null, CancellationToken ct = default)
    {
        // In production, the SQL store is the source of truth for status updates.
        // This method supports Redis-based fast-path updates.
        var cmd = await _commandStore.GetByIdAsync(commandId, ct);
        if (cmd != null)
        {
            cmd.Status = status;
            if (status == CommandStatus.Acked) cmd.AckedAt = DateTime.UtcNow;
            cmd.ResultMessage = resultMessage ?? cmd.ResultMessage;
            await _commandStore.SaveAsync(cmd, ct);
        }
    }
}
