using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.ProcessedEventTests;

/// <summary>
/// Tests that <see cref="ProcessedEvent.ProcessedAt"/> is stored using the canonical
/// ISO-8601 "O" encoding (UTC, 7 fractional digits, trailing Z), matching
/// <see cref="OutboxMessage.OccurredAt"/>'s encoding.
/// </summary>
public sealed class ProcessedAtEncoding : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public ProcessedAtEncoding()
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

        services.AddDbContext<FoundryDbContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetRequiredService<OutboxSaveChangesInterceptor>());
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FoundryDbContext>());

        return services.BuildServiceProvider();
    }

    private async Task<ProcessedEvent> PersistAsync(DateTimeOffset processedAt)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();

        ProcessedEvent entity = ProcessedEvent.For(Guid.NewGuid(), "Foundry.TestHandler", processedAt);
        dbContext.Set<ProcessedEvent>().Add(entity);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return entity;
    }

    private async Task<string?> ReadProcessedAtRawAsync(Guid eventId)
    {
        await using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT processed_at FROM processed_events WHERE upper(event_id) = upper($eventId)";
        cmd.Parameters.AddWithValue("$eventId", eventId.ToString("D"));
        object? result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result is DBNull or null ? null : (string)result;
    }

    private async Task<string?> ReadOccurredAtRawAsync(Guid messageId)
    {
        await using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT occurred_at FROM outbox_messages WHERE upper(id) = upper($messageId)";
        cmd.Parameters.AddWithValue("$messageId", messageId.ToString("D"));
        object? result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result is DBNull or null ? null : (string)result;
    }

    [Fact]
    public async Task WhenProcessedEventIsPersisted_ProcessedAtIsCanonicalIso8601()
    {
        // Arrange
        DateTimeOffset processedAt = new DateTimeOffset(2025, 6, 1, 12, 30, 45, 123, TimeSpan.Zero).AddTicks(4567);
        ProcessedEvent entity = await PersistAsync(processedAt);

        // Act — read the raw stored string via raw SQL
        string? rawValue = await ReadProcessedAtRawAsync(entity.EventId);

        // Assert — canonical ISO 8601 "O" shape: T at index 10, ends with Z, 7 fractional digits
        rawValue.ShouldNotBeNull();
        rawValue[10].ShouldBe('T');
        rawValue.ShouldEndWith("Z");
        rawValue.ShouldMatch(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$");
    }

    [Fact]
    public async Task WhenProcessedEventIsPersisted_ProcessedAtMatchesOutboxMessageOccurredAtEncoding()
    {
        // Arrange — an arbitrary UTC instant
        DateTimeOffset instant = new DateTimeOffset(2025, 6, 1, 12, 30, 45, TimeSpan.Zero);

        // Persist a ProcessedEvent for this instant
        ProcessedEvent processedEntity = await PersistAsync(instant);

        // Persist an OutboxMessage at the same instant to compare raw stored values
        await using AsyncServiceScope seedScope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext seedContext = seedScope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        OutboxMessage outboxMessage = OutboxMessage.Create(new EncodingTestEvent("x"), instant);
        seedContext.Set<OutboxMessage>().Add(outboxMessage);
        await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — read both raw strings directly from the shared connection
        string? processedAtRaw = await ReadProcessedAtRawAsync(processedEntity.EventId);
        string? occurredAtRaw = await ReadOccurredAtRawAsync(outboxMessage.Id);

        // Assert — both raw strings are identical for the same instant
        processedAtRaw.ShouldNotBeNull();
        occurredAtRaw.ShouldNotBeNull();
        processedAtRaw.ShouldBe(occurredAtRaw);
    }

    [Fact]
    public async Task WhenProcessedEventIsPersistedAndReloaded_ProcessedAtRoundTripsSurvives()
    {
        // Arrange
        DateTimeOffset processedAt = new DateTimeOffset(2025, 9, 17, 8, 0, 0, TimeSpan.Zero);
        ProcessedEvent persisted = await PersistAsync(processedAt);

        // Act — reload through EF
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        FoundryDbContext dbContext = scope.ServiceProvider.GetRequiredService<FoundryDbContext>();
        ProcessedEvent? reloaded = await dbContext.Set<ProcessedEvent>()
            .FirstOrDefaultAsync(
                e => e.EventId == persisted.EventId,
                TestContext.Current.CancellationToken);

        // Assert — DateTimeOffset survives the round-trip equal
        reloaded.ShouldNotBeNull();
        reloaded.ProcessedAt.ShouldBe(processedAt);
    }
}

internal sealed record EncodingTestEvent(string Name) : IIntegrationEvent;
