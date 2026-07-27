using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Events;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxSaveChangesInterceptorTests;

public sealed class SavingChangesAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly IntegrationEventCollector _collector;
    private readonly FoundryDbContext _dbContext;

    public SavingChangesAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _collector = new IntegrationEventCollector();

        ServiceCollection services = new();
        services.AddSingleton(_collector);

        _serviceProvider = services.BuildServiceProvider();

        OutboxSaveChangesInterceptor interceptor = new(_serviceProvider);

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenEventEnqueuedBeforeSave_OutboxMessagePersistedInSameTransaction()
    {
        // Arrange
        _collector.Enqueue(MakeEvent());

        // Act
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenEventEnqueuedAndSaveCalledTwice_ExactlyOneOutboxMessagePersisted()
    {
        // Arrange
        _collector.Enqueue(MakeEvent());

        // Act
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenNoEventsEnqueued_SaveChangesWritesNoOutboxMessages()
    {
        // Arrange — no events enqueued

        // Act
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTwoEventsEnqueuedBeforeSave_BothOutboxMessagesPersisted()
    {
        // Arrange
        _collector.Enqueue(MakeEvent());
        _collector.Enqueue(MakeEvent());

        // Act
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        List<OutboxMessage> messages = await _dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);

        messages.Count.ShouldBe(2);
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
