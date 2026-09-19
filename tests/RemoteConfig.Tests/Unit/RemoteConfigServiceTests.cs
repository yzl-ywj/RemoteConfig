using System.Text.Json;
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
/// Unit tests for RemoteConfigService — config push, rollback, versioning.
/// </summary>
public class RemoteConfigServiceTests
{
    private readonly Mock<IConfigRepository> _configRepoMock;
    private readonly Mock<IAuditLogger> _auditLoggerMock;
    private readonly Mock<ICommandDispatcher> _dispatcherMock;
    private readonly Mock<ICommandStore> _commandStoreMock;
    private readonly RemoteConfigService _service;
    private readonly RemoteConfigOptions _options;

    public RemoteConfigServiceTests()
    {
        _configRepoMock = new Mock<IConfigRepository>();
        _auditLoggerMock = new Mock<IAuditLogger>();
        _dispatcherMock = new Mock<ICommandDispatcher>();
        _commandStoreMock = new Mock<ICommandStore>();

        _options = new RemoteConfigOptions
        {
            MaxConfigSizeBytes = 1024,
            MaxVersionsPerDevice = 10,
            DefaultCommandTimeoutSeconds = 30,
            PerDeviceRateLimit = 5,
            PerTenantRateLimit = 50
        };

        var opts = Microsoft.Extensions.Options.Options.Create(_options);
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<RemoteConfigService>();

        _service = new RemoteConfigService(
            _configRepoMock.Object,
            _auditLoggerMock.Object,
            _dispatcherMock.Object,
            opts,
            logger);
    }

    [Fact]
    public async Task PushConfig_WhenNoExistingConfig_CreatesVersion1()
    {
        // Arrange
        _configRepoMock.Setup(r => r.GetActiveAsync("dev-001", default))
            .ReturnsAsync((DeviceConfig?)null);
        _configRepoMock.Setup(r => r.CreateAsync(It.IsAny<DeviceConfig>(), default))
            .ReturnsAsync((DeviceConfig c, CancellationToken ct) => c);
        _configRepoMock.Setup(r => r.GetVersionCountAsync("dev-001", default))
            .ReturnsAsync(1);

        var config = JsonDocument.Parse("{\"samplingRate\":5000}");

        // Act
        var result = await _service.PushConfigAsync("dev-001", config, "admin@tietoevry.com",
            description: "Initial config");

        // Assert
        result.Version.Should().Be(1);
        result.DeviceId.Should().Be("dev-001");
        result.Status.Should().Be(ConfigStatus.Active);
        result.Description.Should().Be("Initial config");
        result.PreviousVersion.Should().BeNull();

        _configRepoMock.Verify(r => r.CreateAsync(It.Is<DeviceConfig>(c =>
            c.Version == 1 && c.ChangedBy == "admin@tietoevry.com"), default), Times.Once);
        _auditLoggerMock.Verify(a => a.WriteAsync(It.IsAny<AuditEntry>(), default), Times.Once);
    }

    [Fact]
    public async Task PushConfig_WhenConfigExists_CreatesNewVersionAndSupersedesOld()
    {
        // Arrange
        var existing = new DeviceConfig
        {
            ConfigId = Guid.NewGuid(),
            DeviceId = "dev-001",
            Version = 3,
            Configuration = JsonDocument.Parse("{\"old\":\"value\"}"),
            Status = ConfigStatus.Active
        };

        _configRepoMock.Setup(r => r.GetActiveAsync("dev-001", default))
            .ReturnsAsync(existing);
        _configRepoMock.Setup(r => r.CreateAsync(It.IsAny<DeviceConfig>(), default))
            .ReturnsAsync((DeviceConfig c, CancellationToken ct) => c);
        _configRepoMock.Setup(r => r.UpdateStatusAsync(existing.ConfigId, ConfigStatus.Superseded, null, default))
            .ReturnsAsync(true);
        _configRepoMock.Setup(r => r.GetVersionCountAsync("dev-001", default))
            .ReturnsAsync(4);

        var newConfig = JsonDocument.Parse("{\"new\":\"value\"}");

        // Act
        var result = await _service.PushConfigAsync("dev-001", newConfig, "admin@tietoevry.com");

        // Assert
        result.Version.Should().Be(4);
        result.PreviousVersion.Should().Be(3);
        _configRepoMock.Verify(r => r.UpdateStatusAsync(
            existing.ConfigId, ConfigStatus.Superseded, null, default), Times.Once);
    }

    [Fact]
    public async Task PushConfig_WhenPayloadTooLarge_ThrowsInvalidOperation()
    {
        // Arrange
        var bigConfig = JsonDocument.Parse("{\"data\":\"" + new string('x', 2000) + "\"}");

        // Act & Assert
        var act = async () => await _service.PushConfigAsync("dev-001", bigConfig, "admin");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*too large*");
    }

    [Fact]
    public async Task Rollback_WhenTargetVersionExists_CreatesNewVersionWithOldConfig()
    {
        // Arrange
        var targetVersion = new DeviceConfig
        {
            ConfigId = Guid.NewGuid(),
            DeviceId = "dev-001",
            Version = 2,
            Configuration = JsonDocument.Parse("{\"key\":\"oldValue\"}"),
            Status = ConfigStatus.Superseded
        };

        var current = new DeviceConfig
        {
            ConfigId = Guid.NewGuid(),
            DeviceId = "dev-001",
            Version = 5,
            Configuration = JsonDocument.Parse("{\"key\":\"newValue\"}"),
            Status = ConfigStatus.Active
        };

        _configRepoMock.Setup(r => r.GetVersionsAsync("dev-001", 0, 100, default))
            .ReturnsAsync(new List<DeviceConfig> { current, targetVersion }.AsReadOnly());
        _configRepoMock.Setup(r => r.GetActiveAsync("dev-001", default))
            .ReturnsAsync(current);
        _configRepoMock.Setup(r => r.CreateAsync(It.IsAny<DeviceConfig>(), default))
            .ReturnsAsync((DeviceConfig c, CancellationToken ct) => c);
        _configRepoMock.Setup(r => r.UpdateStatusAsync(targetVersion.ConfigId, ConfigStatus.RolledBack, "Rollback", default))
            .ReturnsAsync(true);
        _configRepoMock.Setup(r => r.GetVersionCountAsync("dev-001", default))
            .ReturnsAsync(6);

        // Act
        var result = await _service.RollbackAsync("dev-001", 2, "Bug in v5", "admin@tietoevry.com");

        // Assert
        result.Version.Should().Be(6); // New version created
        _configRepoMock.Verify(r => r.UpdateStatusAsync(
            targetVersion.ConfigId, ConfigStatus.RolledBack, "Rollback", default), Times.Once);
        _auditLoggerMock.Verify(a => a.WriteAsync(
            It.Is<AuditEntry>(e => e.Action == "CONFIG_ROLLBACK"), default), Times.Once);
    }

    [Fact]
    public async Task Rollback_WhenTargetVersionNotFound_ThrowsInvalidOperation()
    {
        // Arrange
        _configRepoMock.Setup(r => r.GetVersionsAsync("dev-001", 0, 100, default))
            .ReturnsAsync(new List<DeviceConfig>().AsReadOnly());

        // Act & Assert
        var act = async () => await _service.RollbackAsync("dev-001", 99, "reason", "admin");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task GetVersionHistory_ReturnsVersionInfos()
    {
        // Arrange
        var versions = new List<DeviceConfig>
        {
            new() { ConfigId = Guid.NewGuid(), DeviceId = "dev-001", Version = 3, Status = ConfigStatus.Active, Description = "v3", ChangedBy = "admin", CreatedAt = DateTime.UtcNow },
            new() { ConfigId = Guid.NewGuid(), DeviceId = "dev-001", Version = 2, Status = ConfigStatus.Superseded, Description = "v2", ChangedBy = "admin", CreatedAt = DateTime.UtcNow.AddHours(-1) },
            new() { ConfigId = Guid.NewGuid(), DeviceId = "dev-001", Version = 1, Status = ConfigStatus.RolledBack, Description = "v1", ChangedBy = "system", CreatedAt = DateTime.UtcNow.AddDays(-1) }
        };

        _configRepoMock.Setup(r => r.GetVersionsAsync("dev-001", 0, 20, default))
            .ReturnsAsync(versions.AsReadOnly());

        // Act
        var result = await _service.GetVersionHistoryAsync("dev-001");

        // Assert
        result.Should().HaveCount(3);
        result[0].Version.Should().Be(3);
        result[0].Status.Should().Be(ConfigStatus.Active);
        result[2].Status.Should().Be(ConfigStatus.RolledBack);
    }
}
