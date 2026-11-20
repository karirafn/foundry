using Foundry.Modules.Credentials.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Features.CredentialsReactions;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.CredentialsReactions.UsageLimitReachedHandlerTests;

public sealed class HandleAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventProcessor _processor = new();
    private readonly CapturingLogger<UsageLimitReachedHandler> _logger = new();

    public HandleAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenResetsAtChanges_PersistsTheResetTime()
    {
        // Arrange
        await SeedSettingsAsync();
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);

        // Act
        await DeliverAsync(resetsAt);

        // Assert
        DispatchPause settings = await ReloadSettingsAsync();
        settings.UsageLimitResetsAt.ShouldBe(resetsAt);
    }

    [Fact]
    public async Task WhenResetsAtChanges_DeliversDispatchPausedDirectly()
    {
        // Arrange
        await SeedSettingsAsync();
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);

        // Act
        await DeliverAsync(resetsAt);

        // Assert
        DispatchPaused paused = _processor.Captured.ShouldHaveSingleItem().ShouldBeOfType<DispatchPaused>();
        paused.ResetsAt.ShouldBe(resetsAt);
    }

    [Fact]
    public async Task WhenResetsAtChanges_WritesNoOutboxRow()
    {
        // Arrange
        await SeedSettingsAsync();

        // Act
        await DeliverAsync(DateTimeOffset.UtcNow.AddHours(2));

        // Assert
        (await _dbContext.Set<Foundry.Shared.Infrastructure.Outbox.OutboxMessage>()
            .CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task WhenRedelivered_DeliversDispatchPausedOnlyOnce()
    {
        // Arrange
        await SeedSettingsAsync();
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        await DeliverAsync(resetsAt);

        // Act
        await DeliverAsync(resetsAt);

        // Assert
        _processor.Captured.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenRedelivered_LeavesTheResetTimeUnchanged()
    {
        // Arrange
        await SeedSettingsAsync();
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        await DeliverAsync(resetsAt);

        // Act
        await DeliverAsync(resetsAt);

        // Assert
        DispatchPause settings = await ReloadSettingsAsync();
        settings.UsageLimitResetsAt.ShouldBe(resetsAt);
    }

    [Fact]
    public async Task WhenNoSettingsRow_LogsAWarning()
    {
        // Act
        await DeliverAsync(DateTimeOffset.UtcNow.AddHours(2));

        // Assert
        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WhenNoSettingsRow_DeliversNothing()
    {
        // Act
        await DeliverAsync(DateTimeOffset.UtcNow.AddHours(2));

        // Assert
        _processor.Captured.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDispatchPausedDeliveryFails_StillPersistsTheResetTime()
    {
        // Arrange
        await SeedSettingsAsync();
        DateTimeOffset resetsAt = DateTimeOffset.UtcNow.AddHours(2);
        UsageLimitReachedHandler sut = new(_dbContext, new ThrowingIntegrationEventProcessor(), _logger);

        // Act
        await sut.HandleAsync(new UsageLimitReached(resetsAt), TestContext.Current.CancellationToken);

        // Assert
        DispatchPause settings = await ReloadSettingsAsync();
        settings.UsageLimitResetsAt.ShouldBe(resetsAt);
    }

    private Task DeliverAsync(DateTimeOffset resetsAt)
    {
        UsageLimitReachedHandler sut = new(_dbContext, _processor, _logger);
        return sut.HandleAsync(new UsageLimitReached(resetsAt), TestContext.Current.CancellationToken);
    }

    private async Task SeedSettingsAsync()
    {
        _dbContext.Set<DispatchPause>().Add(DispatchPause.Create());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<DispatchPause> ReloadSettingsAsync()
    {
        _dbContext.ChangeTracker.Clear();
        return await _dbContext.Set<DispatchPause>()
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private sealed class CapturingIntegrationEventProcessor : IIntegrationEventProcessor
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task ProcessAsync(Guid eventId, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            _captured.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingIntegrationEventProcessor : IIntegrationEventProcessor
    {
        public Task ProcessAsync(Guid eventId, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
            => throw new InvalidOperationException("SignalR unavailable");
    }
}
