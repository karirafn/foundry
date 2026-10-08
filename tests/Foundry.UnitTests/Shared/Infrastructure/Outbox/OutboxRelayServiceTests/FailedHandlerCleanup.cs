using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxRelayServiceTests;

public sealed class FailedHandlerCleanup : IAsyncDisposable
{
    private const string ConflictingHandlerName = "Test.ConflictingHandler";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public FailedHandlerCleanup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _serviceProvider = BuildServiceProvider(_connection);

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
        services.AddScoped<IIntegrationEventProcessor>(sp => new ConflictingSaveProcessor(
            sp.GetRequiredService<FoundryDbContext>(),
            sp.GetRequiredService<IntegrationEventCollector>()));

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddSingleton(Options.Create(new OutboxOptions { MaxAttempts = 2 }));

        return services.BuildServiceProvider();
    }

    private OutboxRelayService CreateSut()
    {
        IOptions<OutboxOptions> options = _serviceProvider.GetRequiredService<IOptions<OutboxOptions>>();
        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new OutboxRelayService(scopeFactory, options, NullLogger<OutboxRelayService>.Instance);
    }

    private async Task SeedAsync(OutboxMessage message, ProcessedEvent? processedEvent = null)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        dbContext.Set<OutboxMessage>().Add(message);

        if (processedEvent is not null)
        {
            dbContext.Set<ProcessedEvent>().Add(processedEvent);
        }

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<OutboxMessage>> LoadOutboxAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        return await dbContext.Set<OutboxMessage>()
            .OrderBy(m => m.OccurredAt)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenHandlerLeavesConflictingEntityTracked_FailureIsRecordedAndTickDoesNotThrow()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(new TestRelayEvent("Conflict"), DateTimeOffset.UtcNow);
        await SeedAsync(message, ProcessedEvent.For(message.Id, ConflictingHandlerName, DateTimeOffset.UtcNow));

        OutboxRelayService sut = CreateSut();

        // Act
        await Should.NotThrowAsync(() => sut.TickForTest(TestContext.Current.CancellationToken));

        // Assert
        List<OutboxMessage> rows = await LoadOutboxAsync();
        OutboxMessage persisted = rows.ShouldHaveSingleItem();
        persisted.ShouldSatisfyAllConditions(
            () => persisted.Attempts.ShouldBe(1),
            () => persisted.Error.ShouldNotBeNullOrEmpty(),
            () => persisted.ProcessedAt.ShouldBeNull());
    }

    [Fact]
    public async Task WhenHandlerFails_EventsItEnqueuedAreNotCommitted()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(new TestRelayEvent("Conflict"), DateTimeOffset.UtcNow);
        await SeedAsync(message, ProcessedEvent.For(message.Id, ConflictingHandlerName, DateTimeOffset.UtcNow));

        OutboxRelayService sut = CreateSut();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        List<OutboxMessage> rows = await LoadOutboxAsync();
        rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenHandlerKeepsFailing_RowIsDeadLetteredAfterMaxAttempts()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(new TestRelayEvent("Conflict"), DateTimeOffset.UtcNow);
        await SeedAsync(message, ProcessedEvent.For(message.Id, ConflictingHandlerName, DateTimeOffset.UtcNow));

        OutboxRelayService sut = CreateSut();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);
        await sut.TickForTest(TestContext.Current.CancellationToken);
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        List<OutboxMessage> rows = await LoadOutboxAsync();
        rows.ShouldHaveSingleItem().Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task WhenFirstMessageFails_LaterMessageInSameBatchIsStillPublished()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OutboxMessage failing = OutboxMessage.Create(new TestRelayEvent("Conflict"), now.AddMinutes(-5));
        OutboxMessage healthy = OutboxMessage.Create(new TestRelayEvent("Healthy"), now);
        await SeedAsync(failing, ProcessedEvent.For(failing.Id, ConflictingHandlerName, now));
        await SeedAsync(healthy);

        OutboxRelayService sut = CreateSut();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        List<OutboxMessage> rows = await LoadOutboxAsync();
        rows[1].ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenHandlerThrowsWithPendingEvents_PendingEventsAreNotCommitted()
    {
        // Arrange
        OutboxMessage message = OutboxMessage.Create(new TestRelayEvent("EnqueueThenThrow"), DateTimeOffset.UtcNow);
        await SeedAsync(message);

        OutboxRelayService sut = CreateSut();

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        List<OutboxMessage> rows = await LoadOutboxAsync();
        rows.ShouldHaveSingleItem().Attempts.ShouldBe(1);
    }

    /// <summary>
    /// "Conflict" enqueues an event then saves a row that violates a unique constraint;
    /// "EnqueueThenThrow" enqueues an event and throws; any other event succeeds.
    /// </summary>
    private sealed class ConflictingSaveProcessor(
        FoundryDbContext dbContext,
        IntegrationEventCollector collector) : IIntegrationEventProcessor
    {
        public async Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
        {
            if (@event is TestRelayEvent { Name: "EnqueueThenThrow" })
            {
                collector.Enqueue(new TestRelayEvent("Pending"));
                throw new InvalidOperationException("Handler failed after enqueuing.");
            }

            if (@event is not TestRelayEvent { Name: "Conflict" })
            {
                return;
            }

            collector.Enqueue(new TestRelayEvent("Drained"));
            dbContext.Set<ProcessedEvent>()
                .Add(ProcessedEvent.For(eventId, ConflictingHandlerName, DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
