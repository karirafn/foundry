using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Infrastructure.WorkerRunConfigurationTests;

public sealed class PersistSuppliedReviewFacts : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistSuppliedReviewFacts()
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
    public async Task WhenRunHasReviewFacts_ReloadsTheSameFacts()
    {
        // Arrange
        ReviewFacts facts = ReviewFactsBuilder.With(
            [
                new ChangedPath(ChangeStatus.Modified, "src/App.cs"),
                new ChangedPath(ChangeStatus.Deleted, "tests/AppTests.cs"),
                new ChangedPath(ChangeStatus.Added, "docs/guide.md"),
            ],
            AcceptanceCriteria.Extract("## Acceptance Criteria\n\n- [ ] one\n- two\n"));
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        run.RecordSuppliedReviewFacts(facts);

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        ReviewFacts reloaded = result.ShouldNotBeNull().SuppliedReviewFacts.ShouldNotBeNull();
        reloaded.ShouldBe(facts);
        reloaded.ProductionChangedWithoutTests.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenRunHasNoReviewFacts_ReloadsNull()
    {
        // Arrange
        StartingRun run = StartingRun.Begin(IssueId.New(), WorkerRunId.New());

        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerRun? result = await _dbContext
            .Set<WorkerRun>()
            .FindAsync([run.Id], TestContext.Current.CancellationToken);

        // Assert
        result.ShouldNotBeNull().SuppliedReviewFacts.ShouldBeNull();
    }
}
