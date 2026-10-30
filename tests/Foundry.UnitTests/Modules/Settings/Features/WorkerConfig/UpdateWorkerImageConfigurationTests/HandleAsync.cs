using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.UpdateWorkerImageConfigurationTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private readonly SqliteConnection _connection;

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
    }

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();

        await using FoundryDbContext setup = CreateDbContext();
        await setup.Database.EnsureCreatedAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private FoundryDbContext CreateDbContext()
    {
        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new FoundryDbContext(options);
    }

    private const string DefaultImageTag = "foundry-worker:local";

    private static WorkerImageConfiguration CreateConfig(
        string imageTag = DefaultImageTag,
        bool buildEnabled = true,
        bool dotnet = false,
        bool angular = false,
        bool chromium = false,
        bool docker = false) =>
        WorkerImageConfiguration.Create(
            imageTag: imageTag,
            buildEnabled: buildEnabled,
            installDotnet: dotnet,
            installAngular: angular,
            installChromium: chromium,
            installDocker: docker)
            .Match(v => v, e => throw new InvalidOperationException(e.Message));

    private async Task SeedSettingsAsync(WorkerImageConfiguration? config = null)
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();
        if (config is not null)
        {
            settings.UpdateWorkerImageConfiguration(config);
        }

        seedDb.Set<GlobalSettings>().Add(settings);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenFlagsChange_PublishesWorkerImageConfigurationChangedEvent()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        dispatcher.Captured.ShouldContain(e => e is WorkerImageConfigurationChanged);
    }

    [Fact]
    public async Task WhenFlagsChange_DoesNotSetImageBuildStatusToBuilding()
    {
        // Arrange — status transition to Building is owned by WorkerImageRebuildService, not this handler
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }

    [Fact]
    public async Task WhenOnlyInstallChromiumChanges_PublishesWorkerImageConfigurationChangedEvent()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: true,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        dispatcher.Captured.ShouldContain(e => e is WorkerImageConfigurationChanged);
    }

    [Fact]
    public async Task WhenOnlyInstallDockerChanges_PublishesWorkerImageConfigurationChangedEvent()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: true);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        dispatcher.Captured.ShouldContain(e => e is WorkerImageConfigurationChanged);
    }

    [Fact]
    public async Task WhenFlagsUnchanged_DoesNotPublishEvent()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig(dotnet: true));

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenFlagsUnchanged_DoesNotSetBuildingStatus()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig(dotnet: true));

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: true,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ImageBuildState.ShouldNotBeOfType<ImageBuildState.Building>();
    }

    [Fact]
    public async Task WhenFlagsChange_PersistsUpdatedFlagsToDatabase()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: true,
            InstallAngular: true,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.WorkerImageConfiguration.InstallDotnet.ShouldBeTrue(),
            () => stored.WorkerImageConfiguration.InstallAngular.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenFlagsChange_ReturnsUpdatedSummary()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: true,
            InstallAngular: true,
            InstallChromium: false,
            InstallDocker: true);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.InstallDotnet.ShouldBeTrue(),
            () => success.Value.InstallAngular.ShouldBeTrue(),
            () => success.Value.InstallChromium.ShouldBeFalse(),
            () => success.Value.InstallDocker.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.NotFoundCode);
    }

    [Fact]
    public async Task WhenImageTagChanges_PublishesWorkerImageConfigurationChangedEvent()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: "my-registry/foundry-worker:v2",
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        dispatcher.Captured.ShouldContain(e => e is WorkerImageConfigurationChanged);
    }

    [Fact]
    public async Task WhenImageTagChanges_PersistsNewTagToDatabase()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: "my-registry/foundry-worker:v2",
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.WorkerImageConfiguration.ImageTag.ShouldBe("my-registry/foundry-worker:v2");
    }

    [Fact]
    public async Task WhenImageTagChanges_ReturnsSummaryWithNewTag()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: "my-registry/foundry-worker:v2",
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.ImageTag.ShouldBe("my-registry/foundry-worker:v2");
    }

    [Fact]
    public async Task WhenBuildEnabledChanges_PublishesWorkerImageConfigurationChangedEvent()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig(buildEnabled: true));

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: false,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        dispatcher.Captured.ShouldContain(e => e is WorkerImageConfigurationChanged);
    }

    [Fact]
    public async Task WhenBuildEnabledChanges_ReturnsSummaryWithNewBuildEnabled()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig(buildEnabled: true));

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: DefaultImageTag,
            BuildEnabled: false,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.BuildEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenEmptyImageTagProvided_ReturnsValidationError()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: "",
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.EmptyImageTagCode);
    }

    [Fact]
    public async Task WhenLatestImageTagProvided_ReturnsValidationError()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: ":latest",
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.LatestImageTagCode);
    }

    [Fact]
    public async Task WhenEmptyImageTagProvided_DoesNotPublishEvent()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig());

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: "   ",
            BuildEnabled: true,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSummaryReturned_IncludesImageTagAndBuildEnabled()
    {
        // Arrange
        await SeedSettingsAsync(CreateConfig(imageTag: "foundry-worker:v1", buildEnabled: false));

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            ImageTag: "foundry-worker:v1",
            BuildEnabled: false,
            InstallDotnet: false,
            InstallAngular: false,
            InstallChromium: false,
            InstallDocker: false);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.ImageTag.ShouldBe("foundry-worker:v1"),
            () => success.Value.BuildEnabled.ShouldBeFalse());
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
