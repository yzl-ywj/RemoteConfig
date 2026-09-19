using Moq;
using Xunit;
using FluentAssertions;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;
using RemoteConfig.Infrastructure.Services;

namespace RemoteConfig.Tests.Unit;

/// <summary>
/// Unit tests for CommandService — send, batch, cancel, status.
/// </summary>
public class CommandServiceTests
{
    private readonly Mock<ICommandDispatcher> _dispatcherMock;
    private readonly Mock<ICommandStore> _storeMock;
    private readonly Mock<IAuditLogger> _auditMock;
    private readonly CommandService _service;
    private readonly RemoteConfigOptions _options;

    public CommandServiceTests()
    {
        _dispatcherMock = new Mock<ICommandDispatcher>();
        _storeMock = new Mock<ICommandStore>();
        _auditMock = new Mock<IAuditLogger>();

        _options = new RemoteConfigOptions
        {
            DefaultCommandTimeoutSeconds = 30,
            PerDeviceRateLimit = 10,
            PerTenantRateLimit = 100
        };

        var opts = Microsoft.Extensions.Options.Options.Create(_options);
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandService>();

        _service = new CommandService(
            _dispatcherMock.Object,
            _storeMock.Object,
            _auditMock.Object,
            opts,
            logger);
    }

    [Fact]
    public async Task SendCommand_ValidRequest_DispatchesAndReturnsCommand()
    {
        // Arrange
        _storeMock.Setup(s => s.SaveAsync(It.IsAny<CommandRequest>(), default))
            .Returns(Task.CompletedTask);
        _dispatcherMock.Setup(d => d.DispatchAsync(It.IsAny<CommandRequest>(), default))
            .ReturnsAsync((CommandRequest c, CancellationToken ct) => c);
        _auditMock.Setup(a => a.WriteAsync(It.IsAny<AuditEntry>(), default))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.SendCommandAsync(
            "dev-001", CommandType.Reboot, "admin@tietoevry.com",
            payload: "{\"delay\":5}", CommandDeliveryMode.CloudToDevice, 30);

        // Assert
        result.DeviceId.Should().Be("dev-001");
        result.CommandType.Should().Be(CommandType.Reboot);
        result.Status.Should().Be(CommandStatus.Queued);
        result.Payload.Should().Be("{\"delay\":5}");
        result.TimeoutSeconds.Should().Be(30);

        _dispatcherMock.Verify(d => d.DispatchAsync(
            It.Is<CommandRequest>(c => c.CommandType == CommandType.Reboot), default), Times.Once);
        _auditMock.Verify(a => a.WriteAsync(
            It.Is<AuditEntry>(e => e.Action == "COMMAND_SENT"), default), Times.Once);
    }

    [Fact]
    public async Task SendCommand_DirectMethod_SetsCorrectDeliveryMode()
    {
        // Arrange
        _storeMock.Setup(s => s.SaveAsync(It.IsAny<CommandRequest>(), default))
            .Returns(Task.CompletedTask);
        _dispatcherMock.Setup(d => d.DispatchAsync(It.IsAny<CommandRequest>(), default))
            .ReturnsAsync((CommandRequest c, CancellationToken ct) => c);

        // Act
        var result = await _service.SendCommandAsync(
            "dev-002", CommandType.RestartApp, "admin","payload",
            mode: CommandDeliveryMode.DirectMethod, timeoutSeconds: 60);

        // Assert
        result.DeliveryMode.Should().Be(CommandDeliveryMode.DirectMethod);
        result.TimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public async Task SendBatchCommand_RespectsRateLimit()
    {
        // Arrange
        var deviceIds = Enumerable.Range(1, 5).Select(i => $"dev-{i:000}").ToList();
        _storeMock.Setup(s => s.SaveAsync(It.IsAny<CommandRequest>(), default))
            .Returns(Task.CompletedTask);
        _dispatcherMock.Setup(d => d.DispatchAsync(It.IsAny<CommandRequest>(), default))
            .ReturnsAsync((CommandRequest c, CancellationToken ct) => c);

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await _service.SendBatchCommandAsync(
            deviceIds, CommandType.Reboot, "admin", rateLimitPerSec: 10);
        sw.Stop();

        // Assert
        results.Should().HaveCount(5);
        // With 10/sec = 100ms between commands, 4 intervals = ~400ms minimum
        sw.ElapsedMilliseconds.Should().BeGreaterThan(300);
    }

    [Fact]
    public async Task CancelCommand_WhenPending_CallsDispatcherCancel()
    {
        // Arrange
        var cmdId = Guid.NewGuid();
        var pendingCmd = new CommandRequest
        {
            CommandId = cmdId,
            DeviceId = "dev-001",
            CommandType = CommandType.Reboot,
            Status = CommandStatus.Queued
        };

        _storeMock.Setup(s => s.GetByIdAsync(cmdId, default))
            .ReturnsAsync(pendingCmd);
        _dispatcherMock.Setup(d => d.CancelAsync(cmdId, default))
            .ReturnsAsync(true);
        _auditMock.Setup(a => a.WriteAsync(It.IsAny<AuditEntry>(), default))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CancelCommandAsync(cmdId, "admin");

        // Assert
        result.Should().BeTrue();
        _dispatcherMock.Verify(d => d.CancelAsync(cmdId, default), Times.Once);
    }

    [Fact]
    public async Task CancelCommand_WhenNotFound_ReturnsFalse()
    {
        // Arrange
        _storeMock.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((CommandRequest?)null);

        // Act
        var result = await _service.CancelCommandAsync(Guid.NewGuid(), "admin");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetCommandStatus_WhenExists_ReturnsResult()
    {
        // Arrange
        var cmdId = Guid.NewGuid();
        var cmd = new CommandRequest
        {
            CommandId = cmdId,
            DeviceId = "dev-001",
            CommandType = CommandType.Reboot,
            Status = CommandStatus.Acked,
            AckedAt = DateTime.UtcNow
        };

        _storeMock.Setup(s => s.GetByIdAsync(cmdId, default))
            .ReturnsAsync(cmd);

        // Act
        var result = await _service.GetCommandStatusAsync(cmdId);

        // Assert
        result.Should().NotBeNull();
        result!.Status.Should().Be(CommandStatus.Acked);
        result.CommandType.Should().Be(CommandType.Reboot);
        result.AckedAt.Should().NotBeNull();
    }
}
