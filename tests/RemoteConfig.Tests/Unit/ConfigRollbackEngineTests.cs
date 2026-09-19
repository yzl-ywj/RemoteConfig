using System.Text.Json;
using Moq;
using Xunit;
using FluentAssertions;
using RemoteConfig.Core.Enums;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Core.Models;
using RemoteConfig.Infrastructure.Services;

namespace RemoteConfig.Tests.Unit;

/// <summary>
/// Unit tests for ConfigRollbackEngine — diff generation.
/// </summary>
public class ConfigRollbackEngineTests
{
    private readonly ConfigRollbackEngine _engine;
    private readonly Mock<IConfigRepository> _repoMock;
    private readonly Mock<IRemoteConfigService> _svcMock;
    private readonly Mock<Microsoft.Extensions.Logging.ILogger<ConfigRollbackEngine>> _loggerMock;

    public ConfigRollbackEngineTests()
    {
        _repoMock = new Mock<IConfigRepository>();
        _svcMock = new Mock<IRemoteConfigService>();
        _loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<ConfigRollbackEngine>>();

        _engine = new ConfigRollbackEngine(
            _repoMock.Object, _svcMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void GetDiff_WhenIdenticalConfigs_ReturnsNoDifferences()
    {
        // Arrange
        var oldConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 1,
            Configuration = JsonDocument.Parse("{\"samplingRate\":5000,\"threshold\":42}")
        };
        var newConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 2,
            Configuration = JsonDocument.Parse("{\"samplingRate\":5000,\"threshold\":42}")
        };

        // Act
        var diff = _engine.GetDiff(oldConfig, newConfig);

        // Assert
        diff.Should().Be("(no differences)");
    }

    [Fact]
    public void GetDiff_WhenValuesChanged_ReturnsChangeLines()
    {
        // Arrange
        var oldConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 1,
            Configuration = JsonDocument.Parse("{\"samplingRate\":5000,\"threshold\":42}")
        };
        var newConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 2,
            Configuration = JsonDocument.Parse("{\"samplingRate\":10000,\"threshold\":42}")
        };

        // Act
        var diff = _engine.GetDiff(oldConfig, newConfig);

        // Assert
        diff.Should().Contain("samplingRate");
        diff.Should().Contain("5000");
        diff.Should().Contain("10000");
        diff.Should().NotContain("threshold"); // unchanged
    }

    [Fact]
    public void GetDiff_WhenPropertyRemoved_ReturnsRemovedMarker()
    {
        // Arrange
        var oldConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 1,
            Configuration = JsonDocument.Parse("{\"keep\":1,\"removeMe\":99}")
        };
        var newConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 2,
            Configuration = JsonDocument.Parse("{\"keep\":1}")
        };

        // Act
        var diff = _engine.GetDiff(oldConfig, newConfig);

        // Assert
        diff.Should().Contain("removeMe");
        diff.Should().Contain("[REMOVED]");
    }

    [Fact]
    public void GetDiff_WhenPropertyAdded_ReturnsAddedMarker()
    {
        // Arrange
        var oldConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 1,
            Configuration = JsonDocument.Parse("{\"keep\":1}")
        };
        var newConfig = new DeviceConfig
        {
            DeviceId = "dev-001",
            Version = 2,
            Configuration = JsonDocument.Parse("{\"keep\":1,\"newProp\":\"hello\"}")
        };

        // Act
        var diff = _engine.GetDiff(oldConfig, newConfig);

        // Assert
        diff.Should().Contain("newProp");
        diff.Should().Contain("[NEW]");
        diff.Should().Contain("hello");
    }
}
