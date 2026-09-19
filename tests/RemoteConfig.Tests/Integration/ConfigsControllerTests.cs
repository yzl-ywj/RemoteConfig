using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using RemoteConfig.Api.Controllers;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Configuration;

namespace RemoteConfig.Tests.Integration;

/// <summary>
/// Integration-style tests for ConfigsController using mocked services.
/// </summary>
public class ConfigsControllerTests
{
    private readonly Mock<IRemoteConfigService> _configSvcMock;
    private readonly Mock<IConfigRollback> _rollbackMock;
    private readonly Mock<ICommandStore> _storeMock;
    private readonly ConfigsController _controller;
    private readonly RemoteConfigOptions _options;

    private readonly Mock<IConfigRepository> _configRepoMock;

    public ConfigsControllerTests()
    {
        _configSvcMock = new Mock<IRemoteConfigService>();
        _rollbackMock = new Mock<IConfigRollback>();
        _storeMock = new Mock<ICommandStore>();
        _configRepoMock = new Mock<IConfigRepository>();

        _options = new RemoteConfigOptions
        {
            MaxConfigSizeBytes = 65536,
            PerDeviceRateLimit = 10
        };

        var opts = Options.Create(_options);
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigsController>();

        _controller = new ConfigsController(
            _configSvcMock.Object, _rollbackMock.Object,
            _storeMock.Object, _configRepoMock.Object, opts, logger);

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
    public async Task GetActive_WhenConfigExists_ReturnsOk()
    {
        // Arrange
        var config = new DeviceConfig
        {
            ConfigId = Guid.NewGuid(),
            DeviceId = "dev-001",
            Version = 3,
            Configuration = JsonDocument.Parse("{\"key\":\"value\"}"),
            Status = ConfigStatus.Active
        };

        _configSvcMock.Setup(s => s.GetActiveConfigAsync("dev-001", default))
            .ReturnsAsync(config);

        // Act
        var result = await _controller.GetActive("dev-001", default);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        okResult!.Value.Should().Be(config);
    }

    [Fact]
    public async Task GetActive_WhenNotFound_Returns404()
    {
        // Arrange
        _configSvcMock.Setup(s => s.GetActiveConfigAsync("dev-999", default))
            .ReturnsAsync((DeviceConfig?)null);

        // Act
        var result = await _controller.GetActive("dev-999", default);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PushConfig_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new PushConfigRequest
        {
            Configuration = "{\"samplingRate\":5000}",
            Description = "Test config"
        };

        var created = new DeviceConfig
        {
            ConfigId = Guid.NewGuid(),
            DeviceId = "dev-001",
            Version = 1,
            Configuration = JsonDocument.Parse(request.Configuration),
            Description = "Test config",
            Status = ConfigStatus.Active,
            ChangedBy = "admin@tietoevry.com"
        };

        _configSvcMock.Setup(s => s.PushConfigAsync(
            "dev-001", It.IsAny<JsonDocument>(), "admin@tietoevry.com",
            "Test config", null, default))
            .ReturnsAsync(created);

        // Act
        var result = await _controller.PushConfig("dev-001", request, default);

        // Assert
        result.Result.Should().BeOfType<CreatedAtActionResult>();
        var createdResult = result.Result as CreatedAtActionResult;
        createdResult!.Value.Should().Be(created);
    }

    [Fact]
    public async Task PushConfig_InvalidJson_ReturnsBadRequest()
    {
        // Arrange
        var request = new PushConfigRequest
        {
            Configuration = "{invalid json!!!"
        };

        // Act
        var result = await _controller.PushConfig("dev-001", request, default);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Rollback_ValidRequest_ReturnsOk()
    {
        // Arrange
        var rolledBack = new DeviceConfig
        {
            ConfigId = Guid.NewGuid(),
            DeviceId = "dev-001",
            Version = 5,
            Configuration = JsonDocument.Parse("{\"key\":\"oldValue\"}"),
            Status = ConfigStatus.Active
        };

        _configSvcMock.Setup(s => s.RollbackAsync(
            "dev-001", 2, "Bug fix", "admin@tietoevry.com", default))
            .ReturnsAsync(rolledBack);

        // Act
        var result = await _controller.Rollback(
            "dev-001", 2, new RollbackRequest { Reason = "Bug fix" }, default);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Rollback_NotFound_Returns404()
    {
        // Arrange
        _configSvcMock.Setup(s => s.RollbackAsync(
            "dev-001", 99, It.IsAny<string>(), It.IsAny<string>(), default))
            .ThrowsAsync(new InvalidOperationException("Version 99 not found"));

        // Act
        var result = await _controller.Rollback(
            "dev-001", 99, new RollbackRequest { Reason = "test" }, default);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
