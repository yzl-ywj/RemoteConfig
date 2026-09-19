using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;
using FluentAssertions;
using RemoteConfig.Api.Controllers;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;

namespace RemoteConfig.Tests.Integration;

/// <summary>
/// Integration-style tests for CommandsController using mocked services.
/// </summary>
public class CommandsControllerTests
{
    private readonly Mock<ICommandService> _cmdSvcMock;
    private readonly CommandsController _controller;

    public CommandsControllerTests()
    {
        _cmdSvcMock = new Mock<ICommandService>();

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandsController>();

        _controller = new CommandsController(_cmdSvcMock.Object, logger);

        // Simulate authenticated user
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "admin@tietoevry.com")
                    }, "mock"))
            }
        };
    }

    [Fact]
    public async Task SendCommand_ValidRequest_ReturnsAccepted()
    {
        // Arrange
        var request = new SendCommandRequest
        {
            DeviceId = "dev-001",
            CommandType = CommandType.Reboot,
            Payload = "{\"delay\":5}",
            TimeoutSeconds = 30
        };

        var dispatched = new CommandRequest
        {
            CommandId = Guid.NewGuid(),
            DeviceId = "dev-001",
            CommandType = CommandType.Reboot,
            Payload = "{\"delay\":5}",
            Status = CommandStatus.Queued,
            IssuedBy = "admin@tietoevry.com"
        };

        _cmdSvcMock.Setup(s => s.SendCommandAsync(
            "dev-001", CommandType.Reboot, "admin@tietoevry.com",
            "{\"delay\":5}", CommandDeliveryMode.CloudToDevice, 30, default))
            .ReturnsAsync(dispatched);

        // Act
        var result = await _controller.SendCommand(request, default);

        // Assert
        result.Result.Should().BeOfType<AcceptedResult>();
        var accepted = result.Result as AcceptedResult;
        accepted!.Value.Should().Be(dispatched);
    }

    [Fact]
    public async Task GetCommand_WhenExists_ReturnsOk()
    {
        // Arrange
        var cmdId = Guid.NewGuid();
        var result_data = new CommandResult
        {
            CommandId = cmdId,
            DeviceId = "dev-001",
            CommandType = CommandType.Reboot,
            Status = CommandStatus.Acked,
            AckedAt = DateTime.UtcNow
        };

        _cmdSvcMock.Setup(s => s.GetCommandStatusAsync(cmdId, default))
            .ReturnsAsync(result_data);

        // Act
        var result = await _controller.GetCommand(cmdId, default);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetCommand_WhenNotFound_Returns404()
    {
        // Arrange
        _cmdSvcMock.Setup(s => s.GetCommandStatusAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((CommandResult?)null);

        // Act
        var result = await _controller.GetCommand(Guid.NewGuid(), default);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CancelCommand_WhenCancelled_ReturnsNoContent()
    {
        // Arrange
        var cmdId = Guid.NewGuid();
        _cmdSvcMock.Setup(s => s.CancelCommandAsync(cmdId, "admin@tietoevry.com", default))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.CancelCommand(cmdId, default);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task BatchCommand_ValidRequest_ReturnsAccepted()
    {
        // Arrange
        var request = new BatchCommandRequest
        {
            DeviceIds = new List<string> { "dev-001", "dev-002", "dev-003" },
            CommandType = CommandType.RestartApp,
            RateLimitPerSec = 5
        };

        var results = request.DeviceIds.Select(id => new CommandRequest
        {
            CommandId = Guid.NewGuid(),
            DeviceId = id,
            CommandType = CommandType.RestartApp,
            Status = CommandStatus.Queued
        }).ToList();

        _cmdSvcMock.Setup(s => s.SendBatchCommandAsync(
            It.IsAny<IEnumerable<string>>(), CommandType.RestartApp,
            "admin@tietoevry.com", null, 5, default))
            .ReturnsAsync(results.AsReadOnly());

        // Act
        var result = await _controller.BatchCommand(request, default);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
    }
}
