using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Infrastructure.Messaging;

/// <summary>
/// BackgroundService that consumes command results from Service Bus.
/// Updates command status in the store and handles retries/dead-lettering.
/// </summary>
public class CommandResultProcessor : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusProcessor _processor;
    private readonly ICommandStore _commandStore;
    private readonly IAuditLogger _auditLogger;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<CommandResultProcessor> _logger;

    public CommandResultProcessor(
        IOptions<ServiceBusOptions> options,
        ICommandStore commandStore,
        IAuditLogger auditLogger,
        ILogger<CommandResultProcessor> logger)
    {
        _options = options.Value;
        _commandStore = commandStore;
        _auditLogger = auditLogger;
        _logger = logger;

        _client = new ServiceBusClient(_options.ConnectionString);
        _processor = _client.CreateProcessor(
            _options.CommandQueueName + "-results",
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 4,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
            });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("CommandResultProcessor started");

        // Keep running until cancelled
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        await _processor.StopProcessingAsync();
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var json = Encoding.UTF8.GetString(args.Message.Body);
            var result = JsonSerializer.Deserialize<CommandResultEnvelope>(json);

            if (result == null)
            {
                _logger.LogWarning("Received null command result, dead-lettering");
                await args.DeadLetterMessageAsync(args.Message, "NullPayload", "Empty result envelope");
                return;
            }

            var cmd = await _commandStore.GetByIdAsync(result.CommandId, args.CancellationToken);
            if (cmd == null)
            {
                _logger.LogWarning("Command {CommandId} not found in store", result.CommandId);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            // Update status
            var newStatus = result.Success ? CommandStatus.Acked : CommandStatus.Failed;
            cmd.Status = newStatus;
            cmd.ResultMessage = result.ResultMessage;
            cmd.ErrorMessage = result.Success ? null : result.ErrorMessage;

            if (newStatus == CommandStatus.Acked)
                cmd.AckedAt = DateTime.UtcNow;

            await _commandStore.SaveAsync(cmd, args.CancellationToken);

            // Audit
            await _auditLogger.WriteAsync(new AuditEntry
            {
                DeviceId = cmd.DeviceId ?? "unknown",
                Action = newStatus == CommandStatus.Acked ? "COMMAND_ACKED" : "COMMAND_FAILED",
                ChangedBy = "system",
                Metadata = new Dictionary<string, string>
                {
                    ["commandId"] = cmd.CommandId.ToString(),
                    ["result"] = result.ResultMessage ?? result.ErrorMessage ?? ""
                }
            }, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            _logger.LogDebug("Command {CommandId} → {Status}", cmd.CommandId, newStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing command result message");
            await args.AbandonMessageAsync(args.Message,cancellationToken: args.CancellationToken);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus error in CommandResultProcessor: {ErrorSource}",
            args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _processor.DisposeAsync();
        await _client.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    // ─── Internal DTO ──────────────────────────────────
    private class CommandResultEnvelope
    {
        public Guid CommandId { get; set; }
        public bool Success { get; set; }
        public string? ResultMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
