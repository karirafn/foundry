using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Runs.CountConsecutiveTransientRunsTests;

public sealed class WhenCountingConsecutiveTransientRuns : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private const int MaxAttempts = 2;

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public WhenCountingConsecutiveTransientRuns()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<int> QueryAsync(Guid issueId, int maxAttempts = MaxAttempts)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IWorkerRunQueries sut = scope.ServiceProvider.GetRequiredService<IWorkerRunQueries>();

        return await sut.CountConsecutiveTransientRunsAsync(
            issueId,
            maxAttempts,
            TestContext.Current.CancellationToken);
    }

    private async Task SeedFailedRunAsync(
        IssueId issueId,
        FailureReason reason,
        DateTimeOffset createdAt)
    {
        // No POST endpoint exists for worker runs — seed directly through DbContext.
        // The factory chain always uses DateTimeOffset.UtcNow for CreatedAt; override via
        // the EF entry to control the timestamp for ordering tests.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-transient-test"),
            BranchName.From("feat/42-test"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            reason,
            branchNameOrNull: null,
            containerOutput: null);

        dbContext.Set<WorkerRun>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Back-date CreatedAt to the requested timestamp for ordering tests.
        dbContext.Entry(failed).Property(r => r.CreatedAt).CurrentValue = createdAt;
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedCompletedRunAsync(IssueId issueId, DateTimeOffset createdAt)
    {
        // No POST endpoint exists for worker runs — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-completed-test"),
            BranchName.From("feat/42-test"),
            MonitoredRepositoryId.New());

        CompletedRun completed = active.Complete(
            exitCode: 0,
            branchName: BranchName.From("feat/42-test"),
            pullRequestUrl: null);

        dbContext.Set<WorkerRun>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Back-date CreatedAt to the requested timestamp for ordering tests.
        dbContext.Entry(completed).Property(r => r.CreatedAt).CurrentValue = createdAt;
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenNoRunsExist_ReturnsZero()
    {
        // Arrange
        Guid issueId = Guid.NewGuid();

        // Act
        int result = await QueryAsync(issueId);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public async Task WhenOneTransientRun_ReturnsOne()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        await SeedFailedRunAsync(issueId, new FailureReason.TransientApiError(), BaseTime);

        // Act
        int result = await QueryAsync(issueId.Value);

        // Assert
        result.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTwoTransientRunsThenOlderNonTransientFailed_ReturnsTwo()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        await SeedFailedRunAsync(issueId, new FailureReason.NonZeroExit(1), BaseTime.AddMinutes(-3));
        await SeedFailedRunAsync(issueId, new FailureReason.TransientApiError(), BaseTime.AddMinutes(-2));
        await SeedFailedRunAsync(issueId, new FailureReason.TransientApiError(), BaseTime.AddMinutes(-1));

        // Act
        int result = await QueryAsync(issueId.Value);

        // Assert
        result.ShouldBe(2);
    }

    [Fact]
    public async Task WhenNewestRunIsNonTransientFailed_ReturnsZero()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        await SeedFailedRunAsync(issueId, new FailureReason.TransientApiError(), BaseTime.AddMinutes(-2));
        await SeedFailedRunAsync(issueId, new FailureReason.NonZeroExit(1), BaseTime.AddMinutes(-1));

        // Act
        int result = await QueryAsync(issueId.Value);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNewestRunIsCompletedRun_ReturnsZero()
    {
        // Arrange
        IssueId issueId = IssueId.New();

        await SeedFailedRunAsync(issueId, new FailureReason.TransientApiError(), BaseTime.AddMinutes(-2));
        await SeedCompletedRunAsync(issueId, BaseTime.AddMinutes(-1));

        // Act
        int result = await QueryAsync(issueId.Value);

        // Assert
        result.ShouldBe(0);
    }
}
