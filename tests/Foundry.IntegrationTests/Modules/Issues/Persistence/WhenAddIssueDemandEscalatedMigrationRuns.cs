using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Persistence;

/// <summary>
/// The AddIssueDemandEscalated migration adds a non-nullable column defaulting to false with no backfill: every issue
/// that existed before it reads as not escalated, even one whose last revision was for review findings. Issues are
/// written with the column, the migration is rolled back (dropping it) and applied again, so the rows predate the
/// column. Runs locally against SQLite.
/// </summary>
public sealed class WhenAddIssueDemandEscalatedMigrationRuns : IAsyncLifetime, IAsyncDisposable
{
    private const string PreviousMigrationId = "20261126143612_AddImplementTierSettings";

    private readonly string _dbPath;
    private readonly MonitoredRepositoryId _repositoryId = MonitoredRepositoryId.New();
    private SqliteConnection _connection = null!;
    private FoundryDbContext _dbContext = null!;

    public WhenAddIssueDemandEscalatedMigrationRuns()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"demand-escalated-migration-{Guid.NewGuid():N}.db");
    }

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly("Foundry.WebApi"))
            .Options;

        _dbContext = new FoundryDbContext(options, DataProtectionProvider.Create("migration-test"));

        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private IssueBuilder Builder(int number) => new IssueBuilder()
        .WithMonitoredRepositoryId(_repositoryId)
        .WithIssueNumber(number);

    [Fact]
    public async Task ExistingIssues_ReadAsNotEscalated()
    {
        // Arrange
        RevisionQueuedIssue escalated = Builder(1).RevisionQueuedFromReviewFindings();
        escalated.DemandEscalated.ShouldBeTrue();
        _dbContext.Set<Issue>().AddRange(escalated, Builder(2).Review(), Builder(3).Detected());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        await _dbContext.Database.MigrateAsync(PreviousMigrationId, TestContext.Current.CancellationToken);

        // Act
        await _dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);

        // Assert
        List<Issue> issues = await _dbContext.Set<Issue>()
            .Where(i => i.MonitoredRepositoryId == _repositoryId)
            .ToListAsync(TestContext.Current.CancellationToken);
        issues.Count.ShouldBe(3);
        issues.ShouldAllBe(i => !i.DemandEscalated);
    }
}
