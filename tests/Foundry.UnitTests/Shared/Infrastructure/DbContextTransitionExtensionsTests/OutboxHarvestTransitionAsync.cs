using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.DbContextTransitionExtensionsTests;

/// <summary>
/// Verifies the D2 "harvest" ordering in <see cref="DbContextTransitionExtensions.TransitionAsync"/>:
/// domain events are dispatched inside the transaction, and the trailing SaveChanges flushes any
/// enqueued integration events into outbox_messages atomically with the state change.
/// </summary>
public sealed class OutboxHarvestTransitionAsync : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public OutboxHarvestTransitionAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection);

        // Create schema once — all tests share the same in-memory connection
        using IServiceScope scope = _serviceProvider.CreateScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        dbContext.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ServiceProvider BuildServiceProvider(SqliteConnection connection)
    {
        ServiceCollection services = new();

        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventDispatcher, OutboxIntegrationEventDispatcher>();

        // Register the domain event dispatcher and the test bridge handler
        services.AddScoped<IDomainEventHandler<IssueQueued>, IssueQueuedBridgeHandler>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });

        return services.BuildServiceProvider();
    }

    private static DetectedIssue CreateDetectedIssue()
    {
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueAuthor author = IssueAuthor.Create("octocat").ValueOrThrow();
        ProviderUrl url = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

        return DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: author,
            url: url,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task WhenDomainHandlerEnqueuesIntegrationEvent_OutboxRowPersistedAtomicallyWithStateChange()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        DetectedIssue detected = CreateDetectedIssue();
        dbContext.Add(detected);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        QueuedIssue queued = detected.Enqueue();

        // Act
        await dbContext.TransitionAsync(detected, queued, domainEventDispatcher, TestContext.Current.CancellationToken);

        // Assert — state change persisted
        dbContext.ChangeTracker.Clear();
        Issue? result = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken);
        result.ShouldBeOfType<QueuedIssue>();

        // Assert — exactly one outbox row written by the harvest save
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenDomainHandlerThrows_TransactionRollsBackStateChangeAndOutboxRow()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IDomainEventDispatcher domainEventDispatcher = new ThrowingDomainEventDispatcher();

        DetectedIssue detected = CreateDetectedIssue();
        dbContext.Add(detected);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        IssueId issueId = detected.Id;
        QueuedIssue queued = detected.Enqueue();

        // Act — should propagate the exception
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await dbContext.TransitionAsync(
                detected,
                queued,
                domainEventDispatcher,
                TestContext.Current.CancellationToken));

        // Assert — state change rolled back: the original DetectedIssue row is still present
        // (the Remove + SaveChanges inside the transaction was rolled back), and the row has
        // not been replaced by a QueuedIssue.
        dbContext.ChangeTracker.Clear();
        Issue? result = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == issueId, TestContext.Current.CancellationToken);
        result.ShouldBeOfType<DetectedIssue>();

        // Assert — no outbox row
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoDomainEventsRaised_StateChangePersistsAndNoOutboxRows()
    {
        // Arrange
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        IDomainEventDispatcher domainEventDispatcher =
            scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        DetectedIssue detected = CreateDetectedIssue();
        dbContext.Add(detected);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Call Enqueue() to get the QueuedIssue, then clear domain events so the dispatcher
        // receives an empty list — the harvest save must still be a no-op for the outbox.
        QueuedIssue queued = detected.Enqueue();
        detected.ClearDomainEvents();

        // Act
        await dbContext.TransitionAsync(detected, queued, domainEventDispatcher, TestContext.Current.CancellationToken);

        // Assert — state change persisted
        dbContext.ChangeTracker.Clear();
        Issue? result = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == queued.Id, TestContext.Current.CancellationToken);
        result.ShouldBeOfType<QueuedIssue>();

        // Assert — no outbox rows
        List<OutboxMessage> messages = await dbContext
            .Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        messages.ShouldBeEmpty();
    }

    /// <summary>
    /// Simulates a bridge domain event handler that enqueues an integration event via
    /// <see cref="IIntegrationEventDispatcher"/>. Models how bridge handlers like
    /// <c>WorkerRunFailedBridgeHandler</c> enqueue integration events inside the transaction.
    /// </summary>
    private sealed class IssueQueuedBridgeHandler(IIntegrationEventDispatcher integrationEventDispatcher)
        : IDomainEventHandler<IssueQueued>
    {
        public Task HandleAsync(IssueQueued @event, CancellationToken cancellationToken)
        {
            // Enqueue a synthetic integration event — content is not important for the harvest test.
            IssueDetected integrationEvent = new(
                @event.MonitoredRepositoryId,
                IssueNumber: 1,
                Title: "bridged",
                Body: "body",
                Author: "user",
                Url: "https://github.com/org/repo/issues/1",
                Labels: [],
                IssueKindLabel: "foundry",
                DetectedAt: DateTimeOffset.UtcNow);

            return integrationEventDispatcher.DispatchAsync([integrationEvent], cancellationToken);
        }
    }

    /// <summary>
    /// Domain event dispatcher that always throws to verify transaction rollback on handler failure.
    /// </summary>
    private sealed class ThrowingDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated domain handler failure.");
    }
}
