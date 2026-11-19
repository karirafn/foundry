using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Features.DispatchPauses;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

using Foundry.UnitTests.Modules.Settings.Features;

namespace Foundry.UnitTests.Modules.Workers.Features.DispatchPauses.ResumeDispatchTests;

public sealed class HandleAsync : IAsyncLifetime
{
    private static GlobalSettingsQueries SettingsQueries(FoundryDbContext dbContext) =>
        new(dbContext, new StubWebhookIngressProbe(), new DispatchPauseQueries(dbContext));

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

    [Fact]
    public async Task WhenSettingsExist_ResumesDispatchAndReturnsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            DispatchPause pause = DispatchPause.Create();
            pause.PauseDispatch();
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext), dispatcher);
        ResumeDispatch.Command command = new();

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Success success =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Success>();
        success.Value.IsDispatchPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSettingsExist_PersistsResumeToDatabase()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            DispatchPause pause = DispatchPause.Create();
            pause.PauseDispatch();
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ResumeDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext), dispatcher);
            ResumeDispatch.Command command = new();
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause? stored = await assertDb.Set<DispatchPause>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.IsDispatchPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSettingsExist_PublishesDispatchResumedEvent()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            DispatchPause pause = DispatchPause.Create();
            pause.PauseDispatch();
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext), dispatcher);
        ResumeDispatch.Command command = new();

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is DispatchResumed);
    }

    [Fact]
    public async Task WhenSettingsNotFound_ReturnsNotFoundError()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext), dispatcher);
        ResumeDispatch.Command command = new();

        // Act
        Result<GlobalSettingsSummary> result = await sut.HandleAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Result<GlobalSettingsSummary>.Failure failure =
            result.ShouldBeOfType<Result<GlobalSettingsSummary>.Failure>();
        failure.Error.Code.ShouldBe(DispatchPauseErrors.SettingsNotFoundCode);
    }

    [Fact]
    public async Task WhenSettingsNotFound_DoesNotPublishEvent()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext), dispatcher);
        ResumeDispatch.Command command = new();

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBothPauseReasonsSet_PublishesDispatchResumed()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            DispatchPause pause = DispatchPause.Create();
            pause.PauseDispatch();
            pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext), dispatcher);
        ResumeDispatch.Command command = new();

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldContain(e => e is DispatchResumed);
    }

    [Fact]
    public async Task WhenBothPauseReasonsSet_ClearsBoth()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            seedDb.Set<GlobalSettings>().Add(GlobalSettings.Create());
            DispatchPause pause = DispatchPause.Create();
            pause.PauseDispatch();
            pause.SetUsageLimitResetsAt(DateTimeOffset.UtcNow.AddHours(1));
            seedDb.Set<DispatchPause>().Add(pause);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ResumeDispatch.Handler sut = new(dbContext, SettingsQueries(dbContext), dispatcher);
            ResumeDispatch.Command command = new();
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        DispatchPause? stored = await assertDb.Set<DispatchPause>()
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        stored.ShouldNotBeNull();
        stored.ShouldSatisfyAllConditions(
            () => stored.IsDispatchPaused.ShouldBeFalse(),
            () => stored.UsageLimitResetsAt.ShouldBeNull());
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
