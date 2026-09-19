using System.Text;
using System.Text.Json;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using RemoteConfig.Core.Models;

namespace CommandBridge.Function.Services;

/// <summary>
/// Bridges commands consumed from Azure Service Bus to target IoT devices via IoT Hub C2D messages.
/// </summary>
public sealed class CommandForwarder
{
    private readonly ServiceClient _serviceClient;
    private readonly ILogger<CommandForwarder> _logger;

    public CommandForwarder(ServiceClient serviceClient, ILogger<CommandForwarder> logger)
    {
        _serviceClient = serviceClient;
        _logger = logger;
    }

    /// <summary>
    /// Processes a single command message: forwards it as a Cloud-to-Device message to the target device.
    /// </summary>
    public async Task ForwardAsync(string deviceId, byte[] payload, IDictionary<string, string>? properties, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("deviceId is required.", nameof(deviceId));
        if (payload == null || payload.Length == 0)
            throw new ArgumentException("payload must not be empty.", nameof(payload));

        using var c2d = new Microsoft.Azure.Devices.Message(payload)
        {
            Ack = DeliveryAcknowledgement.Full,
            MessageId = Guid.NewGuid().ToString(),
            CreationTimeUtc = DateTime.UtcNow
        };

        if (properties != null)
        {
            foreach (var kv in properties)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key))
                    c2d.Properties[kv.Key] = kv.Value ?? string.Empty;
            }
        }

        _logger.LogInformation("Forwarding C2D command to device {DeviceId}, size={Bytes} bytes", deviceId, payload.Length);
        await _serviceClient.SendAsync(deviceId, c2d).ConfigureAwait(false);
        _logger.LogInformation("C2D command forwarded to device {DeviceId}", deviceId);
    }

    /// <summary>
    /// Parses a Service Bus message body into a CommandRequest.
    /// iot-remote-config's ServiceBusCommandDispatcher serializes CommandRequest directly;
    /// this method is the primary parse path and falls back to the envelope format.
    /// </summary>
    public static CommandRequest? ParseCommand(BinaryData body)
    {
        if (body == null || body.IsEmpty) return null;
        var req = JsonSerializer.Deserialize<CommandRequest>(body);
        if (req != null && !string.IsNullOrWhiteSpace(req.DeviceId)) return req;

        // Fallback: envelope format
        var env = JsonSerializer.Deserialize<CommandEnvelope>(body);
        if (env == null || string.IsNullOrWhiteSpace(env.DeviceId)) return null;
        return new CommandRequest
        {
            CommandId = !string.IsNullOrWhiteSpace(env.CommandId) ? Guid.Parse(env.CommandId) : Guid.NewGuid(),
            DeviceId = env.DeviceId,
            CommandType = !string.IsNullOrWhiteSpace(env.CommandType)
                ? Enum.Parse<RemoteConfig.Core.Enums.CommandType>(env.CommandType, ignoreCase: true)
                : RemoteConfig.Core.Enums.CommandType.Custom,
            Payload = !string.IsNullOrWhiteSpace(env.Payload) ? Encoding.UTF8.GetString(Convert.FromBase64String(env.Payload)) : null
        };
    }

    /// <summary>
    /// Converts the command payload string to bytes for the C2D message body.
    /// </summary>
    public static byte[] EncodePayload(CommandRequest command)
    {
        var raw = command.Payload;
        if (string.IsNullOrEmpty(raw)) return Array.Empty<byte>();
        // Payload is JSON; send as UTF-8 bytes so the device can parse it directly.
        return Encoding.UTF8.GetBytes(raw);
    }
}

/// <summary>
/// Wire format for commands placed on the Service Bus queue by iot-remote-config.
/// </summary>
public sealed class CommandEnvelope
{
    public string? DeviceId { get; set; }
    public string? CommandId { get; set; }
    public string? CommandType { get; set; }
    public string? Payload { get; set; } // base64-encoded binary payload
    public Dictionary<string, string>? Properties { get; set; }
}
