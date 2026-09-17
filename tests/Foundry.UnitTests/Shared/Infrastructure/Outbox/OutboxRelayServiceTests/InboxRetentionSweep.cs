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

/// <summary>
/// Tests for the inbox (processed_events) pruning behaviour added to <see cref="OutboxRelayService"/>.
/// </summary>
public sealed class InboxRetentionSweep : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public InboxRetentionSweep()
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
        services.AddScoped<IIntegrationEventProcessor, NoOpProcessor>();

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        services.AddSingleton(Options.Create(new OutboxOptions
        {
            RetentionWindow = TimeSpan.FromDays(7),
            RetentionSweepInterval = TimeSpan.FromHours(1),
        }));

        return services.BuildServiceProvider();
    }

    private OutboxRelayService CreateSut(OutboxOptions? optionsOverride = null)
    {
        IOptions<OutboxOptions> options = optionsOverride is not null
            ? Options.Create(optionsOverride)
            : _serviceProvider.GetRequiredService<IOptions<OutboxOptions>>();

        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        return new OutboxRelayService(scopeFactory, options, NullLogger<OutboxRelayService>.Instance);
    }

    /// <summary>Seeds a canonical processed_events row through the EF model (uses the "O" converter).</summary>
    private async Task SeedCanonicalProcessedEventAsync(DateTimeOffset processedAt)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        ProcessedEvent entry = ProcessedEvent.For(Guid.NewGuid(), "TestHandler", processedAt);
        dbContext.Set<ProcessedEvent>().Add(entry);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds a processed_events row using raw SQL to simulate legacy encoding
    /// (e.g. "+00:00" suffix rather than "Z" canonical form produced by the "O" converter).
    /// </summary>
    private async Task SeedLegacyProcessedEventAsync(string rawTimestamp)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        string eventId = Guid.NewGuid().ToString();
        await dbContext.Database.ExecuteSqlAsync(
            $"INSERT INTO processed_events (event_id, handler, processed_at) VALUES ({eventId}, 'LegacyHandler', {rawTimestamp})",
            TestContext.Current.CancellationToken);
    }

    private async Task<int> CountProcessedEventsAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        return await dbContext.Set<ProcessedEvent>().CountAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedExpiredPublishedOutboxAsync(DateTimeOffset processedAt)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        OutboxMessage message = OutboxMessage.Create(new TestSweepEvent("old"), DateTimeOffset.UtcNow.AddDays(-30));
        message.MarkPublished(processedAt);
        dbContext.Set<OutboxMessage>().Add(message);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenSweepRuns_InboxRowsOlderThanWindowArePrunedInSameSweep()
    {
        // Arrange — one expired outbox row and one expired processed_events row
        DateTimeOffset expired = DateTimeOffset.UtcNow.AddDays(-8);
        await SeedExpiredPublishedOutboxAsync(expired);
        await SeedCanonicalProcessedEventAsync(expired);

        OutboxRelayService sut = CreateSut();

        // Act — first tick, _lastPruneAt is MinValue so sweep runs
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — both tables emptied
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> outboxRemaining = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        outboxRemaining.ShouldBeEmpty();

        int inboxRemaining = await CountProcessedEventsAsync();
        inboxRemaining.ShouldBe(0);
    }

    [Fact]
    public async Task WhenDedupEntryIsInsideWindow_ItSurvivesAndRedeliveredMessageIsNotReinvoked()
    {
        // Arrange — seed a canonical in-window processed_events entry
        DateTimeOffset inWindow = DateTimeOffset.UtcNow.AddDays(-1);
        Guid eventId = Guid.NewGuid();
        string handlerName = typeof(RecordingRelayEventHandler).FullName!;

        await using AsyncServiceScope seedScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext seedContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ProcessedEvent entry = ProcessedEvent.For(eventId, handlerName, inWindow);
        seedContext.Set<ProcessedEvent>().Add(entry);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Build a service provider that wires the recording handler so redelivery can be observed
        RecordingRelayEventHandler recordingHandler = new();
        ServiceCollection services = new();
        services.AddScoped<IntegrationEventCollector>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<IIntegrationEventProcessor, IntegrationEventProcessor>();
        services.AddScoped<IIntegrationEventHandler<TestRelayEvent>>(_ => recordingHandler);
        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(_connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());
        services.AddSingleton(Options.Create(new OutboxOptions
        {
            RetentionWindow = TimeSpan.FromDays(7),
            RetentionSweepInterval = TimeSpan.FromHours(1),
        }));
        await using ServiceProvider provider = services.BuildServiceProvider();

        IOptions<OutboxOptions> options = provider.GetRequiredService<IOptions<OutboxOptions>>();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        OutboxRelayService sut = new(scopeFactory, options, NullLogger<OutboxRelayService>.Instance);

        // Seed an outbox message for the same event so the relay attempts redelivery
        await using AsyncServiceScope outboxScope = provider.CreateAsyncScope();
        FoundryDbContext outboxContext = outboxScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        TestRelayEvent @event = new("Redelivery");
        OutboxMessage outboxMessage = OutboxMessage.Create(@event, DateTimeOffset.UtcNow.AddDays(-2));
        // Set EventId to match the dedup entry using raw SQL after inserting
        outboxContext.Set<OutboxMessage>().Add(outboxMessage);
        await outboxContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Override the event_id in the outbox row so it matches the dedup entry's eventId
        await outboxContext.Database.ExecuteSqlAsync(
            $"UPDATE outbox_messages SET id = {eventId} WHERE id = {outboxMessage.Id}",
            TestContext.Current.CancellationToken);

        // Act — run sweep
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — the in-window processed_events row still exists
        int inboxCount = await outboxContext.Set<ProcessedEvent>().CountAsync(TestContext.Current.CancellationToken);
        inboxCount.ShouldBe(1);

        // Assert — handler was NOT reinvoked (dedup worked)
        recordingHandler.ReceivedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBatchIsFull_NextTickSweepsAgainWithoutWaitingInterval()
    {
        // Arrange — batch size 2, interval 999 days (never naturally expires), seed 3 expired rows
        OutboxOptions options = new()
        {
            RetentionWindow = TimeSpan.FromDays(7),
            RetentionSweepInterval = TimeSpan.FromDays(999),
            InboxPruneBatchSize = 2,
        };

        DateTimeOffset expired = DateTimeOffset.UtcNow.AddDays(-8);
        await SeedCanonicalProcessedEventAsync(expired);
        await SeedCanonicalProcessedEventAsync(expired);
        await SeedCanonicalProcessedEventAsync(expired);

        OutboxRelayService sut = CreateSut(options);

        // Act — first tick: sweep runs (MinValue), deletes exactly 2 (full batch), leaves _lastPruneAt unadvanced
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — one row remains (batch was full, so one left)
        int afterFirstTick = await CountProcessedEventsAsync();
        afterFirstTick.ShouldBe(1);

        // Act — second tick: despite the huge interval, rearm fires another sweep
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — final row deleted
        int afterSecondTick = await CountProcessedEventsAsync();
        afterSecondTick.ShouldBe(0);
    }

    [Fact]
    public async Task WhenBatchIsUnderfull_NextTickWaitsFullInterval()
    {
        // Arrange — batch size 5, interval 999 days, seed only 2 expired rows (underfull)
        OutboxOptions options = new()
        {
            RetentionWindow = TimeSpan.FromDays(7),
            RetentionSweepInterval = TimeSpan.FromDays(999),
            InboxPruneBatchSize = 5,
        };

        DateTimeOffset expired = DateTimeOffset.UtcNow.AddDays(-8);
        await SeedCanonicalProcessedEventAsync(expired);
        await SeedCanonicalProcessedEventAsync(expired);

        OutboxRelayService sut = CreateSut(options);

        // Act — first tick: sweep runs, deletes 2 (underfull → advances _lastPruneAt)
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — both rows gone
        int afterFirstTick = await CountProcessedEventsAsync();
        afterFirstTick.ShouldBe(0);

        // Arrange — seed more expired rows AFTER the first sweep advanced _lastPruneAt
        await SeedCanonicalProcessedEventAsync(expired);
        await SeedCanonicalProcessedEventAsync(expired);

        // Act — second tick: interval (999 days) has NOT elapsed, sweep is throttled, nothing deleted
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — new rows survive (second sweep was throttled)
        int afterSecondTick = await CountProcessedEventsAsync();
        afterSecondTick.ShouldBe(2);
    }

    [Fact]
    public async Task WhenLegacyRowsAreOlderThanWindow_SweepDeletesThem()
    {
        // Arrange — seed a legacy-encoded row using the real space-separated encoding produced by
        // EF Core's default DateTimeOffset→string on SQLite (space at index 10, "+00:00" suffix).
        // Space (0x20) sorts below canonical "T" (0x54), so out-of-window legacy rows are always
        // older than any cutoff the sweep computes — the ordering invariant this test anchors.
        string legacyTimestamp = DateTimeOffset.UtcNow.AddDays(-8).ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00", System.Globalization.CultureInfo.InvariantCulture);
        await SeedLegacyProcessedEventAsync(legacyTimestamp);

        OutboxRelayService sut = CreateSut();

        // Act — first tick
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — legacy row was pruned
        int remaining = await CountProcessedEventsAsync();
        remaining.ShouldBe(0);
    }

    /// <summary>No-op processor so delivery loop finishes without error during sweep tests.</summary>
    private sealed class NoOpProcessor : IIntegrationEventProcessor
    {
        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
