using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Runs;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Workers.Domain.Entities.States.PublishingRunTests;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Runs.WorkerRunQueriesTests;

public sealed class GetWorkerRunDetailAsync_Toolchain : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;

    public GetWorkerRunDetailAsync_Toolchain()
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
    public async Task ARunWhoseContainerReportedItsToolchain_ShowsTheToolsInReportedOrder()
    {
        // Arrange
        ActiveRun active = PublishingRunFixture.ActiveRun();
        active.RecordToolchain(Toolchain.TryCreate(
        [
            ToolchainEntry.TryCreate("node", "22.22.3")!,
            ToolchainEntry.TryCreate("chromium", "156.0.8078.79")!,
        ]));
        FailedRun failed = active.Fail(new FailureReason.NonZeroExit(1), branchNameOrNull: null);
        await SeedAsync(failed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(failed.Id);

        // Assert
        detail.Toolchain.ShouldNotBeNull().ShouldBe(
        [
            new WorkerRunTool("node", "22.22.3"),
            new WorkerRunTool("chromium", "156.0.8078.79"),
        ]);
    }

    [Fact]
    public async Task ARunWhoseContainerReportedNoToolchain_ShowsNone()
    {
        // Arrange
        FailedRun failed = PublishingRunFixture.ActiveRun().Fail(new FailureReason.NonZeroExit(1), branchNameOrNull: null);
        await SeedAsync(failed);

        // Act
        WorkerRunDetail detail = await GetDetailAsync(failed.Id);

        // Assert
        detail.Toolchain.ShouldBeNull();
    }

    private async Task SeedAsync(WorkerRun run)
    {
        _dbContext.Set<WorkerRun>().Add(run);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private async Task<WorkerRunDetail> GetDetailAsync(WorkerRunId id)
    {
        WorkerRunQueries sut = new(_dbContext);
        Result<WorkerRunDetail> result = await sut.GetWorkerRunDetailAsync(id.Value, TestContext.Current.CancellationToken);
        return result.ShouldBeOfType<Result<WorkerRunDetail>.Success>().Value;
    }
}
