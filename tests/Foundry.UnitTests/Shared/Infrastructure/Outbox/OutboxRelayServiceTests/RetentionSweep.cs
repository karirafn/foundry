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
/// Tests for the retention sweep throttle in <see cref="OutboxRelayService"/>.
/// The relay uses <c>_lastPruneAt</c> (default = DateTimeOffset.MinValue) so the sweep
/// always runs on the very first tick, then is throttled to <c>RetentionSweepInterval</c>.
/// </summary>
public sealed class RetentionSweep : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public RetentionSweep()
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

    private async Task SeedExpiredPublishedAsync(DateTimeOffset processedAt)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        OutboxMessage message = OutboxMessage.Create(new TestSweepEvent("old"), DateTimeOffset.UtcNow.AddDays(-30));
        message.MarkPublished(processedAt);
        dbContext.Set<OutboxMessage>().Add(message);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenFirstTick_SweepRunsAndDeletesExpiredRows()
    {
        // Arrange — seed a published row older than the retention window (7 days)
        DateTimeOffset oldProcessedAt = DateTimeOffset.UtcNow.AddDays(-8);
        await SeedExpiredPublishedAsync(oldProcessedAt);

        OutboxRelayService sut = CreateSut();

        // Act — first tick, _lastPruneAt is MinValue so sweep runs
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — expired row has been pruned
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> remaining = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSweepIntervalNotElapsed_SecondTickSkipsSweep()
    {
        // Arrange — seed an expired row
        DateTimeOffset oldProcessedAt = DateTimeOffset.UtcNow.AddDays(-8);
        await SeedExpiredPublishedAsync(oldProcessedAt);

        // Configure a very long sweep interval so the second tick cannot trigger another sweep
        OutboxOptions options = new()
        {
            RetentionWindow = TimeSpan.FromDays(7),
            RetentionSweepInterval = TimeSpan.FromDays(999),
        };
        OutboxRelayService sut = CreateSut(options);

        // Act — first tick runs the sweep (MinValue), deletes the row
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Seed another expired row AFTER the first sweep ran
        await SeedExpiredPublishedAsync(DateTimeOffset.UtcNow.AddDays(-10));

        // Act — second tick, interval has not elapsed (999 days), sweep is skipped
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — second expired row still exists because the second sweep was throttled
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> remaining = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenSweepIntervalElapsed_SecondTickRunsSweepAgain()
    {
        // Arrange — configure a zero sweep interval so every tick triggers the sweep
        OutboxOptions options = new()
        {
            RetentionWindow = TimeSpan.FromDays(7),
            RetentionSweepInterval = TimeSpan.Zero,
        };
        DateTimeOffset oldProcessedAt = DateTimeOffset.UtcNow.AddDays(-8);
        await SeedExpiredPublishedAsync(oldProcessedAt);

        OutboxRelayService sut = CreateSut(options);

        // Act — first tick prunes the first row
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Seed another expired row after first sweep
        await SeedExpiredPublishedAsync(DateTimeOffset.UtcNow.AddDays(-10));

        // Act — second tick: interval (0) has elapsed, sweep runs again
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — both rows pruned
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> remaining = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenSweepRuns_UnpublishedRowsAreNeverDeleted()
    {
        // Arrange — seed an unpublished row that has exhausted MaxAttempts so the
        // delivery loop skips it; the sweep must also leave it alone (ProcessedAt is null).
        OutboxOptions options = new()
        {
            RetentionWindow = TimeSpan.FromDays(7),
            RetentionSweepInterval = TimeSpan.Zero,
            MaxAttempts = 1,
        };

        await using AsyncServiceScope seedScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext seedContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        OutboxMessage unpublished = OutboxMessage.Create(new TestSweepEvent("old"), DateTimeOffset.UtcNow.AddDays(-30));
        unpublished.RecordFailure("exhausted");     // Attempts == 1 == MaxAttempts → skipped by delivery
        seedContext.Set<OutboxMessage>().Add(unpublished);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        OutboxRelayService sut = CreateSut(options);

        // Act
        await sut.TickForTest(TestContext.Current.CancellationToken);

        // Assert — delivery loop skipped it (exhausted attempts); sweep skipped it (no ProcessedAt)
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        List<OutboxMessage> remaining = await dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.Count.ShouldBe(1);
        remaining[0].ProcessedAt.ShouldBeNull();
    }

    /// <summary>No-op processor so delivery loop finishes without error during sweep tests.</summary>
    private sealed class NoOpProcessor : IIntegrationEventProcessor
    {
        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

internal sealed record TestSweepEvent(string Name) : IIntegrationEvent;
