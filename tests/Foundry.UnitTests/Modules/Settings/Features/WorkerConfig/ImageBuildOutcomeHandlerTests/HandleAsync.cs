using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.Modules.Settings.Features.WorkerConfig;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.WorkerConfig.ImageBuildOutcomeHandlerTests;

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

    private async Task SeedSettingsAsync(GlobalSettings settings)
    {
        await using FoundryDbContext seedDb = CreateDbContext();
        seedDb.Set<GlobalSettings>().Add(settings);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // ── ImageBuildRequestedHandler → BeginImageBuild ──────────────────────

    [Fact]
    public async Task WhenImageBuildRequested_AndSettingsExist_SetsBuildingState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        await SeedSettingsAsync(settings);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildRequestedHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(new ImageBuildRequested(), TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ImageBuildState.ShouldBeOfType<ImageBuildState.Building>();
    }

    [Fact]
    public async Task WhenImageBuildRequested_AndSettingsExist_DirectDeliversImageBuildStartedEvent()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        await SeedSettingsAsync(settings);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildRequestedHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(new ImageBuildRequested(), TestContext.Current.CancellationToken);

        // Assert
        processor.Delivered.ShouldContain(e => e is ImageBuildStarted);
    }

    [Fact]
    public async Task WhenImageBuildRequested_AndSettingsMissing_DoesNotThrowAndDeliversNoEvents()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildRequestedHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(new ImageBuildRequested(), TestContext.Current.CancellationToken);

        // Assert
        processor.Delivered.ShouldBeEmpty();
    }

    // ── ImageBuildSucceededHandler → CompleteImageBuild ───────────────────

    [Fact]
    public async Task WhenImageBuildSucceeded_AndSettingsExist_SetsIdleState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        await SeedSettingsAsync(settings);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildSucceededHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(new ImageBuildSucceeded(), TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ImageBuildState.ShouldBeOfType<ImageBuildState.Idle>();
    }

    [Fact]
    public async Task WhenImageBuildSucceeded_AndSettingsExist_DirectDeliversImageBuildCompletedEvent()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        await SeedSettingsAsync(settings);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildSucceededHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(new ImageBuildSucceeded(), TestContext.Current.CancellationToken);

        // Assert
        processor.Delivered.ShouldContain(e => e is ImageBuildCompleted);
    }

    [Fact]
    public async Task WhenImageBuildSucceeded_AndSettingsMissing_DoesNotThrowAndDeliversNoEvents()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildSucceededHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(new ImageBuildSucceeded(), TestContext.Current.CancellationToken);

        // Assert
        processor.Delivered.ShouldBeEmpty();
    }

    // ── ImageBuildOutcomeFailedHandler → FailImageBuild ───────────────────

    [Fact]
    public async Task WhenImageBuildOutcomeFailed_AndSettingsExist_SetsFailedState()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        await SeedSettingsAsync(settings);

        DateTimeOffset nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildOutcomeFailedHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(
            new ImageBuildOutcomeFailed("error tail", nextRetryAt, Attempt: 1),
            TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        ImageBuildState.Failed failed = stored.ImageBuildState.ShouldBeOfType<ImageBuildState.Failed>();
        failed.ShouldSatisfyAllConditions(
            () => failed.ErrorTail.ShouldBe("error tail"),
            () => failed.Attempt.ShouldBe(1),
            () => failed.NextRetryAt.ShouldBe(nextRetryAt));
    }

    [Fact]
    public async Task WhenImageBuildOutcomeFailed_AndSettingsExist_DirectDeliversImageBuildFailedEvent()
    {
        // Arrange
        GlobalSettings settings = GlobalSettings.Create();
        settings.BeginImageBuild();
        await SeedSettingsAsync(settings);

        DateTimeOffset nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(5);

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildOutcomeFailedHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(
            new ImageBuildOutcomeFailed("error tail", nextRetryAt, Attempt: 1),
            TestContext.Current.CancellationToken);

        // Assert
        processor.Delivered.ShouldContain(e => e is ImageBuildFailed);
    }

    [Fact]
    public async Task WhenImageBuildOutcomeFailed_AndSettingsMissing_DoesNotThrowAndDeliversNoEvents()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventProcessor processor = new();
        ImageBuildOutcomeFailedHandler sut = new(dbContext, processor);

        // Act
        await sut.HandleAsync(
            new ImageBuildOutcomeFailed(ErrorTail: null, NextRetryAt: null, Attempt: 0),
            TestContext.Current.CancellationToken);

        // Assert
        processor.Delivered.ShouldBeEmpty();
    }

    private sealed class CapturingIntegrationEventProcessor : IIntegrationEventProcessor
    {
        private readonly List<IIntegrationEvent> _delivered = [];

        public IReadOnlyList<IIntegrationEvent> Delivered => _delivered;

        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
        {
            _delivered.Add(@event);
            return Task.CompletedTask;
        }
    }
}
