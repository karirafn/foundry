using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.UpdateWorkerImageConfigurationTests;

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
        WorkerImageConfiguration initial = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        await SeedSettingsAsync(initial);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
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
        WorkerImageConfiguration initial = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        await SeedSettingsAsync(initial);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
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
    public async Task WhenFlagsUnchanged_DoesNotPublishEvent()
    {
        // Arrange
        WorkerImageConfiguration config = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        await SeedSettingsAsync(config);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
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
        WorkerImageConfiguration config = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        await SeedSettingsAsync(config);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
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
        WorkerImageConfiguration initial = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        await SeedSettingsAsync(initial);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: true,
            InstallGlab: false,
            InstallGh: false,
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
        WorkerImageConfiguration initial = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
            InstallChromium: false,
            InstallDocker: false);

        await SeedSettingsAsync(initial);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: true,
            InstallAngular: false,
            InstallGlab: true,
            InstallGh: false,
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
            () => success.Value.InstallDotnet.ShouldBeTrue(),
            () => success.Value.InstallGlab.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        UpdateWorkerImageConfiguration.Handler sut = new(dbContext, dispatcher);

        UpdateWorkerImageConfiguration.Command command = new(
            InstallDotnet: false,
            InstallAngular: false,
            InstallGlab: false,
            InstallGh: false,
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
