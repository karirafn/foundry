using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Features.Dispatch;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Events;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Settings.Features.Dispatch.ResumeDispatchTests;

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

    [Fact]
    public async Task WhenSettingsExist_ResumesDispatchAndReturnsSummary()
    {
        // Arrange
        await using (FoundryDbContext seedDb = CreateDbContext())
        {
            GlobalSettings settings = GlobalSettings.Create();
            settings.PauseDispatch();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, dispatcher);
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
            GlobalSettings settings = GlobalSettings.Create();
            settings.PauseDispatch();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (FoundryDbContext dbContext = CreateDbContext())
        {
            CapturingIntegrationEventDispatcher dispatcher = new();
            ResumeDispatch.Handler sut = new(dbContext, dispatcher);
            ResumeDispatch.Command command = new();
            await sut.HandleAsync(command, TestContext.Current.CancellationToken);
        }

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? stored = await assertDb.Set<GlobalSettings>()
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
            GlobalSettings settings = GlobalSettings.Create();
            settings.PauseDispatch();
            seedDb.Set<GlobalSettings>().Add(settings);
            await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, dispatcher);
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
        ResumeDispatch.Handler sut = new(dbContext, dispatcher);
        ResumeDispatch.Command command = new();

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
    public async Task WhenSettingsNotFound_DoesNotPublishEvent()
    {
        // Arrange
        await using FoundryDbContext dbContext = CreateDbContext();
        CapturingIntegrationEventDispatcher dispatcher = new();
        ResumeDispatch.Handler sut = new(dbContext, dispatcher);
        ResumeDispatch.Command command = new();

        // Act
        await sut.HandleAsync(command, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.ShouldBeEmpty();
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
