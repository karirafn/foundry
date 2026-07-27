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

public sealed class WhenIssueHasActiveRun : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public WhenIssueHasActiveRun()
    {
        _factory = new FoundryWebAppFactory();
        _ = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private async Task SeedStartingRunAsync(IssueId issueId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        dbContext.Set<WorkerRun>().Add(starting);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedActiveRunAsync(IssueId issueId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-active-test"),
            BranchName.From("feat/1-active"),
            MonitoredRepositoryId.New());

        dbContext.Set<WorkerRun>().Add(active);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenStartingRun_IncrementsRunCountWithoutTelemetry()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedStartingRunAsync(issueId);

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
    public async Task WhenActiveRun_IncrementsRunCountWithoutTelemetry()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        await SeedActiveRunAsync(issueId);

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
}
