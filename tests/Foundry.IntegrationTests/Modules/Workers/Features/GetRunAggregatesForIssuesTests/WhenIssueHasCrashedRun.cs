using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Features.GetRunAggregatesForIssuesTests;

public sealed class WhenIssueHasCrashedRun : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenIssueHasCrashedRun()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task SeedCrashedFailedRunAsync(IssueId issueId)
    {
        // A FailedRun with no RunResultSummary — represents a container crash before telemetry.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        FailedRun crashed = starting.Fail(new FailureReason.ContainerError("exit -1"));

        dbContext.Set<WorkerRun>().Add(crashed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedCompletedRunAsync(IssueId issueId, long durationMs, int numTurns)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-crash-test"),
            BranchName.From("feat/1-test"),
            MonitoredRepositoryId.New());

        RunResultSummary summary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: durationMs,
            numTurns: numTurns,
            totalCostUsd: null,
            inputTokens: null,
            outputTokens: null);

        CompletedRun completed = active.Complete(exitCode: 0, branchName: null, pullRequestUrl: null, resultSummary: summary);
        dbContext.Set<WorkerRun>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenCrashedRunHasNoTelemetry_ContributesToRunCountButNotTotals()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedCrashedFailedRunAsync(issueId);

        using IServiceScope scope = _factory.Services.CreateScope();
        IWorkerRunQueries sut = scope.ServiceProvider.GetRequiredService<IWorkerRunQueries>();

        // Act
        IReadOnlyDictionary<Guid, RunAggregate> result = await sut.GetRunAggregatesForIssuesAsync(
            [issueId.Value],
            TestContext.Current.CancellationToken);

        // Assert
        result.ContainsKey(issueId.Value).ShouldBeTrue();
        RunAggregate aggregate = result[issueId.Value];
        aggregate.ShouldSatisfyAllConditions(
            () => aggregate.RunCount.ShouldBe(1),
            () => aggregate.DurationMs.ShouldBeNull(),
            () => aggregate.NumTurns.ShouldBeNull(),
            () => aggregate.TotalCostUsd.ShouldBeNull(),
            () => aggregate.InputTokens.ShouldBeNull(),
            () => aggregate.OutputTokens.ShouldBeNull());
    }

    [Fact]
    public async Task WhenMixedRuns_CrashedRunCountsButDoesNotContributeToTotals()
    {
        // Arrange: one completed run with telemetry + one crashed run with no telemetry.
        IssueId issueId = IssueId.New();
        await SeedCompletedRunAsync(issueId, durationMs: 5000, numTurns: 7);
        await SeedCrashedFailedRunAsync(issueId);

        using IServiceScope scope = _factory.Services.CreateScope();
        IWorkerRunQueries sut = scope.ServiceProvider.GetRequiredService<IWorkerRunQueries>();

        // Act
        IReadOnlyDictionary<Guid, RunAggregate> result = await sut.GetRunAggregatesForIssuesAsync(
            [issueId.Value],
            TestContext.Current.CancellationToken);

        // Assert
        RunAggregate aggregate = result[issueId.Value];
        aggregate.ShouldSatisfyAllConditions(
            () => aggregate.RunCount.ShouldBe(2),
            () => aggregate.DurationMs.ShouldBe(5000L),
            () => aggregate.NumTurns.ShouldBe(7));
    }
}
