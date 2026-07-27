using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Contracts.Queries;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Features.GetRunAggregatesForIssuesTests;

public sealed class WhenIssueHasMultipleRuns : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenIssueHasMultipleRuns()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient(); // triggers ConfigureWebHost + EnsureCreated
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task<IssueId> SeedCompletedRunAsync(
        IssueId issueId,
        long durationMs,
        int numTurns,
        decimal totalCostUsd,
        int inputTokens,
        int outputTokens)
    {
        // No POST endpoint for worker runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-agg-test"),
            BranchName.From("feat/1-test"),
            MonitoredRepositoryId.New());

        RunResultSummary summary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: durationMs,
            numTurns: numTurns,
            totalCostUsd: totalCostUsd,
            inputTokens: inputTokens,
            outputTokens: outputTokens);

        CompletedRun completed = active.Complete(exitCode: 0, branchName: null, pullRequestUrl: null, resultSummary: summary);

        dbContext.Set<WorkerRun>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return issueId;
    }

    [Fact]
    public async Task WhenIssueHasTwoCompletedRuns_SumsTotalsCorrectly()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedCompletedRunAsync(
            issueId,
            durationMs: 1000,
            numTurns: 3,
            totalCostUsd: 0.10m,
            inputTokens: 500,
            outputTokens: 200);
        await SeedCompletedRunAsync(
            issueId,
            durationMs: 2000,
            numTurns: 5,
            totalCostUsd: 0.20m,
            inputTokens: 800,
            outputTokens: 300);

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
            () => aggregate.RunCount.ShouldBe(2),
            () => aggregate.DurationMs.ShouldBe(3000L),
            () => aggregate.NumTurns.ShouldBe(8),
            () => aggregate.TotalCostUsd.ShouldBe(0.30m),
            () => aggregate.InputTokens.ShouldBe(1300L),
            () => aggregate.OutputTokens.ShouldBe(500L));
    }
}
