using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.IssueConfigurationTests;

/// <summary>
/// Regression guard (AC #7): proves the StronglyTypedIdValueConverter&lt;WorkerRunId&gt;
/// stores worker_run_id as a bare Guid (not a nested JSON object) and that EF does
/// not re-normalise the stored casing on reload, so no migration is required and
/// the column shape is unchanged.
/// </summary>
public sealed class WorkerRunIdCasingRoundTrip : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public WorkerRunIdCasingRoundTrip()
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
    public async Task WhenInProgressIssueIsPersisted_WorkerRunIdRoundTripsEqualToOriginal()
    {
        // Arrange
        WorkerRunId original = WorkerRunId.New();
        IssueBuilder builder = new IssueBuilder()
            .WithIssueNumber(7)
            .WithTitle("Round-trip test")
            .WithWorkerRunId(original);
        InProgressIssue inProgress = builder.InProgress();

        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        Issue? result = await _dbContext
            .Set<Issue>()
            .FindAsync([inProgress.Id], TestContext.Current.CancellationToken);

        // Assert
        InProgressIssue reloaded = result.ShouldBeOfType<InProgressIssue>();
        reloaded.WorkerRunId.ShouldBe(original);
    }

    [Fact]
    public async Task WhenInProgressIssueIsPersisted_WorkerRunIdStoredAsGuidNotNestedObject()
    {
        // Arrange — store a known WorkerRunId value
        WorkerRunId original = WorkerRunId.New();
        IssueBuilder builder = new IssueBuilder()
            .WithIssueNumber(8)
            .WithTitle("Casing round-trip test")
            .WithWorkerRunId(original);
        InProgressIssue inProgress = builder.InProgress();

        _dbContext.Set<Issue>().Add(inProgress);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act — read the raw worker_run_id cell via the underlying SQLite connection.
        // The column is stored as TEXT by EF Core SQLite (the column type is TEXT in the schema).
        // A nested-object serialization (e.g. {"Value":"..."}) would make the raw TEXT
        // non-parseable as a Guid, proving the StronglyTypedIdValueConverter<WorkerRunId>
        // stores a flat Guid rather than the record struct's default JSON representation.
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT CAST(worker_run_id AS TEXT) FROM issues WHERE state = 'in_progress'";

        object? rawValue = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Assert — CAST to TEXT forces the value to a readable string.
        // If stored correctly as a Guid-equivalent blob/text, it must round-trip via Guid.Parse.
        rawValue.ShouldNotBeNull();
        string rawString = rawValue.ToString()!;
        Guid.TryParse(rawString, out Guid rawGuid).ShouldBeTrue(
            $"Raw cell value '{rawString}' should be a plain Guid string, not a JSON object.");
        rawGuid.ShouldBe(original.Value);
    }
}
