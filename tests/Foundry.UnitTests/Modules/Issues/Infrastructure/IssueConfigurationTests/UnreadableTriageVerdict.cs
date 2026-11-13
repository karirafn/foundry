using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared.Infrastructure;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

public sealed class UnreadableTriageVerdict : IAsyncDisposable
{
    private const string UnreadableVerdict = "{not json";

    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public UnreadableTriageVerdict()
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

    private async Task<TriageBlockedIssue> SeedIssueWithUnreadableVerdictAsync()
    {
        TriageVerdict.Blocked verdict = new(
            IssueKind.Feature,
            [new TriageBlocker(15, "Needs it first.")],
            DecidedAt);
        TriageBlockedIssue seeded = new IssueBuilder().TriageBlockedFromTriage(verdict);
        _dbContext.Set<Issue>().Add(seeded);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        await using SqliteCommand plant = _connection.CreateCommand();
        plant.CommandText = $"UPDATE issues SET triage_verdict = '{UnreadableVerdict}'";
        await plant.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        Issue? reloaded = await _dbContext
            .Set<Issue>()
            .FindAsync([seeded.Id], TestContext.Current.CancellationToken);
        return reloaded.ShouldBeOfType<TriageBlockedIssue>();
    }

    private async Task<object?> StoredVerdictAsync()
    {
        await using SqliteCommand read = _connection.CreateCommand();
        read.CommandText = "SELECT triage_verdict FROM issues";
        return await read.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenTheStoredVerdictCannotBeRead_TheIssueLoadsWithNoVerdict()
    {
        // Arrange
        // (the unreadable row is planted by the helper below)

        // Act
        TriageBlockedIssue loaded = await SeedIssueWithUnreadableVerdictAsync();

        // Assert
        loaded.TriageVerdict.ShouldBeNull();
    }

    [Fact]
    public async Task WhenTheStoredVerdictCannotBeRead_TheNextTransitionErasesTheStoredText()
    {
        // Arrange
        TriageBlockedIssue loaded = await SeedIssueWithUnreadableVerdictAsync();
        (await StoredVerdictAsync()).ShouldBe(UnreadableVerdict);

        // Act
        await _dbContext.TransitionAsync(
            loaded,
            loaded.Unblock(DateTimeOffset.UtcNow),
            new CapturingDomainEventDispatcher(),
            TestContext.Current.CancellationToken);

        // Assert
        (await StoredVerdictAsync()).ShouldBe(DBNull.Value);
    }
}
