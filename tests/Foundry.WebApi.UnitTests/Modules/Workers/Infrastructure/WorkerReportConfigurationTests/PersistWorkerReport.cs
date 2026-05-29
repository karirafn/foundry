using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Infrastructure.WorkerReportConfigurationTests;

public sealed class PersistWorkerReport : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public PersistWorkerReport()
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
    public async Task WhenWorkerReportPersisted_CanBeReloadedWithAllProperties()
    {
        // Arrange — seed a WorkerRun to satisfy the FK constraint
        WorkerRunId workerRunId = WorkerRunId.New();
        StartingRun startingRun = StartingRun.Begin(IssueId.New(), workerRunId);
        _dbContext.WorkerRuns.Add(startingRun);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        WorkerReport report = WorkerReport.Create(
            workerRunId,
            sequenceNumber: 3,
            reportType: "progress",
            content: "Implementing feature...");

        _dbContext.Set<WorkerReport>().Add(report);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        // Act
        WorkerReport? result = await _dbContext
            .Set<WorkerReport>()
            .FindAsync([report.Id], TestContext.Current.CancellationToken);

        // Assert
        WorkerReport reloaded = result.ShouldNotBeNull();
        reloaded.ShouldSatisfyAllConditions(
            () => reloaded.Id.ShouldBe(report.Id),
            () => reloaded.WorkerRunId.ShouldBe(workerRunId),
            () => reloaded.SequenceNumber.ShouldBe(3),
            () => reloaded.ReportType.ShouldBe("progress"),
            () => reloaded.Content.ShouldBe("Implementing feature..."),
            () => reloaded.IngestedAt.ShouldBe(report.IngestedAt));
    }
}
