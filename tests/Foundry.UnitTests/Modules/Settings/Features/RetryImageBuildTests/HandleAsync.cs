using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.RetryImageBuildTests;

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

    private async Task SeedSettingsWithStatusAsync(ImageBuildStatus status)
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();

        switch (status)
        {
            case ImageBuildStatus.Failed:
                settings.FailImageBuild("previous error");
                break;
            case ImageBuildStatus.Building:
                settings.BeginImageBuild();
                break;
            case ImageBuildStatus.Idle:
                break;
        }

        seedDb.Set<GlobalSettings>().Add(settings);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenStatusIsFailed_PublishesWorkerImageConfigurationChangedEvent()
    {
        // Arrange
        await SeedSettingsWithStatusAsync(ImageBuildStatus.Failed);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        RetryImageBuild.Handler sut = new(dbContext, dispatcher);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new RetryImageBuild.Command(),
            TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        dispatcher.Captured.ShouldContain(e => e is WorkerImageConfigurationChanged);
    }

    [Fact]
    public async Task WhenStatusIsFailed_SetsImageBuildStatusToBuilding()
    {
        // Arrange
        await SeedSettingsWithStatusAsync(ImageBuildStatus.Failed);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        RetryImageBuild.Handler sut = new(dbContext, dispatcher);

        // Act
        await sut.HandleAsync(new RetryImageBuild.Command(), TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ImageBuildStatus.ShouldBe(ImageBuildStatus.Building);
    }

    [Fact]
    public async Task WhenStatusIsIdle_ReturnsInvalidStatusError()
    {
        // Arrange
        await SeedSettingsWithStatusAsync(ImageBuildStatus.Idle);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        RetryImageBuild.Handler sut = new(dbContext, dispatcher);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new RetryImageBuild.Command(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidRetryStatusCode);
    }

    [Fact]
    public async Task WhenStatusIsBuilding_ReturnsInvalidStatusError()
    {
        // Arrange
        await SeedSettingsWithStatusAsync(ImageBuildStatus.Building);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        RetryImageBuild.Handler sut = new(dbContext, dispatcher);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new RetryImageBuild.Command(),
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(SettingsErrors.InvalidRetryStatusCode);
    }

    [Fact]
    public async Task WhenStatusIsIdle_DoesNotPublishEvent()
    {
        // Arrange
        await SeedSettingsWithStatusAsync(ImageBuildStatus.Idle);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        RetryImageBuild.Handler sut = new(dbContext, dispatcher);

        // Act
        await sut.HandleAsync(new RetryImageBuild.Command(), TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        RetryImageBuild.Handler sut = new(dbContext, dispatcher);

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            new RetryImageBuild.Command(),
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
