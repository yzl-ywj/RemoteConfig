using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using CommandBridge.Function.Services;

namespace CommandBridge.Function;

/// <summary>
/// Service Bus triggered function that forwards queued commands to IoT Hub as C2D messages.
/// Expected queue name: "iot-commands" (configured via ServiceBusTrigger attribute).
/// </summary>
public class CommandBridgeFunction
{
    private readonly CommandForwarder _forwarder;
    private readonly ILogger<CommandBridgeFunction> _logger;

    public CommandBridgeFunction(CommandForwarder forwarder, ILogger<CommandBridgeFunction> logger)
    {
        _forwarder = forwarder;
        _logger = logger;
    }

    [Function("ForwardCommandToDevice")]
    public async Task RunAsync(
        [ServiceBusTrigger("iot-commands", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var command = CommandForwarder.ParseCommand(message.Body);
        if (command == null || string.IsNullOrWhiteSpace(command.DeviceId))
        {
            _logger.LogWarning("Discarding invalid command (missing deviceId). MessageId={MsgId}", message.MessageId);
            return;
        }

        var payload = CommandForwarder.EncodePayload(command);
        if (payload.Length == 0)
        {
            _logger.LogWarning("Discarding command with empty payload for device {DeviceId}", command.DeviceId);
            return;
        }

        var properties = new Dictionary<string, string>
        {
            ["commandId"] = command.CommandId.ToString(),
            ["commandType"] = command.CommandType.ToString()
        };

        await _forwarder.ForwardAsync(command.DeviceId, payload, properties, cancellationToken).ConfigureAwait(false);
    }
}
