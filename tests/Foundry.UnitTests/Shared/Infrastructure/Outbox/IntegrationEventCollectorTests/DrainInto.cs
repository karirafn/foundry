using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.IntegrationEventCollectorTests;

public sealed class DrainInto : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public DrainInto()
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
    public async Task WhenEventEnqueuedAndDrained_MessageAppearsInContext()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        collector.Enqueue(MakeEvent());

        // Act
        collector.DrainInto(_dbContext);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTwoEventsEnqueuedAndDrained_BothMessagesAppearsInContext()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        collector.Enqueue(MakeEvent());
        collector.Enqueue(MakeEvent());

        // Act
        collector.DrainInto(_dbContext);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenDrained_CollectorIsEmpty()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        collector.Enqueue(MakeEvent());

        // Act
        collector.DrainInto(_dbContext);

        // Assert
        collector.HasPending.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenDrainedTwice_SecondDrainAddsNoMessages()
    {
        // Arrange
        IntegrationEventCollector collector = new();
        collector.Enqueue(MakeEvent());

        // Act
        collector.DrainInto(_dbContext);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        collector.DrainInto(_dbContext);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.Count.ShouldBe(1);
    }

    private static IssueDetected MakeEvent() =>
        new(
            MonitoredRepositoryId.From(Guid.NewGuid()),
            42,
            "Fix the bug",
            "Some body",
            "user",
            "https://github.com/org/repo/issues/42",
            ["bug", "claude"],
            "claude",
            DateTimeOffset.UtcNow);
}
