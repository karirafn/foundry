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

namespace Foundry.IntegrationTests.Modules.Workers.Runs.GetRunAggregatesForIssuesTests;

public sealed class WhenMultipleIssues : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenMultipleIssues()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task SeedCompletedRunAsync(
        IssueId issueId,
        long durationMs,
        int numTurns)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-multi-test"),
            BranchName.From("feat/multi-test"),
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
    public async Task WhenTwoIssuesBothHaveRuns_KeysEachIssueCorrectly()
    {
        // Arrange
        IssueId issueA = IssueId.New();
        IssueId issueB = IssueId.New();
        await SeedCompletedRunAsync(issueA, durationMs: 1000, numTurns: 2);
        await SeedCompletedRunAsync(issueB, durationMs: 9000, numTurns: 10);

        using IServiceScope scope = _factory.Services.CreateScope();
        IWorkerRunQueries sut = scope.ServiceProvider.GetRequiredService<IWorkerRunQueries>();

        // Act
        IReadOnlyDictionary<Guid, RunAggregate> result = await sut.GetRunAggregatesForIssuesAsync(
            [issueA.Value, issueB.Value],
            TestContext.Current.CancellationToken);

        // Assert
        result.Count.ShouldBe(2);
        result[issueA.Value].ShouldSatisfyAllConditions(
            () => result[issueA.Value].RunCount.ShouldBe(1),
            () => result[issueA.Value].DurationMs.ShouldBe(1000L),
            () => result[issueA.Value].NumTurns.ShouldBe(2));
        result[issueB.Value].ShouldSatisfyAllConditions(
            () => result[issueB.Value].RunCount.ShouldBe(1),
            () => result[issueB.Value].DurationMs.ShouldBe(9000L),
            () => result[issueB.Value].NumTurns.ShouldBe(10));
    }

    [Fact]
    public async Task WhenOneIssueHasNoRuns_NotIncludedInResult()
    {
        // Arrange
        IssueId issueWithRun = IssueId.New();
        IssueId issueWithoutRun = IssueId.New();
        await SeedCompletedRunAsync(issueWithRun, durationMs: 2000, numTurns: 3);

        using IServiceScope scope = _factory.Services.CreateScope();
        IWorkerRunQueries sut = scope.ServiceProvider.GetRequiredService<IWorkerRunQueries>();

        // Act
        IReadOnlyDictionary<Guid, RunAggregate> result = await sut.GetRunAggregatesForIssuesAsync(
            [issueWithRun.Value, issueWithoutRun.Value],
            TestContext.Current.CancellationToken);

        // Assert
        result.ContainsKey(issueWithRun.Value).ShouldBeTrue();
        result.ContainsKey(issueWithoutRun.Value).ShouldBeFalse();
    }
}
