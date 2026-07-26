using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.Outbox.OutboxDbContextExtensionsTests;

public sealed class PrunePublishedAsync : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PrunePublishedAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private async Task<OutboxMessage> SeedPublishedAsync(DateTimeOffset processedAt)
    {
        OutboxMessage message = OutboxMessage.Create(new TestPruneEvent("published"), DateTimeOffset.UtcNow.AddDays(-10));
        message.MarkPublished(processedAt);
        _dbContext.Set<OutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return message;
    }

    private async Task<OutboxMessage> SeedUnpublishedAsync()
    {
        OutboxMessage message = OutboxMessage.Create(new TestPruneEvent("unpublished"), DateTimeOffset.UtcNow.AddDays(-20));
        _dbContext.Set<OutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return message;
    }

    [Fact]
    public async Task WhenPublishedRowOlderThanCutoff_RowIsDeleted()
    {
        // Arrange
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        await SeedPublishedAsync(cutoff.AddHours(-1));

        // Act
        int deleted = await _dbContext.PrunePublishedAsync(cutoff, TestContext.Current.CancellationToken);

        // Assert
        deleted.ShouldBe(1);
        List<OutboxMessage> remaining = await _dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPublishedRowNewerThanCutoff_RowIsKept()
    {
        // Arrange
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        await SeedPublishedAsync(cutoff.AddHours(1));

        // Act
        int deleted = await _dbContext.PrunePublishedAsync(cutoff, TestContext.Current.CancellationToken);

        // Assert
        deleted.ShouldBe(0);
        List<OutboxMessage> remaining = await _dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenPublishedRowExactlyAtCutoff_RowIsKept()
    {
        // Arrange — boundary: ProcessedAt == cutoff is not < cutoff, so kept
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        await SeedPublishedAsync(cutoff);

        // Act
        int deleted = await _dbContext.PrunePublishedAsync(cutoff, TestContext.Current.CancellationToken);

        // Assert
        deleted.ShouldBe(0);
        List<OutboxMessage> remaining = await _dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenUnpublishedRowExistsRegardlessOfAge_RowIsNeverDeleted()
    {
        // Arrange — very old unpublished row (no ProcessedAt)
        DateTimeOffset cutoff = DateTimeOffset.UtcNow;
        await SeedUnpublishedAsync();

        // Act
        int deleted = await _dbContext.PrunePublishedAsync(cutoff, TestContext.Current.CancellationToken);

        // Assert
        deleted.ShouldBe(0);
        List<OutboxMessage> remaining = await _dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenMixedRows_OnlyOldPublishedRowsAreDeleted()
    {
        // Arrange
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-7);

        await SeedPublishedAsync(cutoff.AddDays(-1));    // old published — should be deleted
        await SeedPublishedAsync(cutoff.AddHours(1));    // recent published — kept
        await SeedUnpublishedAsync();                    // unpublished — always kept

        // Act
        int deleted = await _dbContext.PrunePublishedAsync(cutoff, TestContext.Current.CancellationToken);

        // Assert
        deleted.ShouldBe(1);
        List<OutboxMessage> remaining = await _dbContext.Set<OutboxMessage>()
            .ToListAsync(TestContext.Current.CancellationToken);
        remaining.Count.ShouldBe(2);
        foreach (OutboxMessage m in remaining)
        {
            (m.ProcessedAt == null || m.ProcessedAt >= cutoff).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task WhenNoRows_ReturnsZero()
    {
        // Arrange
        DateTimeOffset cutoff = DateTimeOffset.UtcNow;

        // Act
        int deleted = await _dbContext.PrunePublishedAsync(cutoff, TestContext.Current.CancellationToken);

        // Assert
        deleted.ShouldBe(0);
    }
}

internal sealed record TestPruneEvent(string Name) : IIntegrationEvent;
