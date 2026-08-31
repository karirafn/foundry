using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Issues.Endpoints.GetIssuesTests;

public sealed class WhenIssueHasWorkerRuns : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly MonitoredRepositoryId RepositoryId = MonitoredRepositoryId.New();

    public WhenIssueHasWorkerRuns()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<IssueId> SeedQueuedIssueAsync(int issueNumber)
    {
        // No POST endpoint for issues — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        FreshQueuedIssue queued = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(issueNumber)
            .WithTitle($"Issue {issueNumber}")
            .WithBody("Body")
            .WithLabels([])
            .FreshQueued();

        dbContext.Set<Issue>().Add(queued);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return queued.Id;
    }

    private async Task SeedCompletedRunAsync(
        IssueId issueId,
        long durationMs,
        int numTurns,
        decimal totalCostUsd)
    {
        // No POST endpoint for worker runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-stats-test"),
            BranchName.From("feat/stats-test"),
            MonitoredRepositoryId.New());

        RunResultSummary summary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: durationMs,
            numTurns: numTurns,
            totalCostUsd: totalCostUsd,
            inputTokens: 100,
            outputTokens: 50);

        CompletedRun completed = active.Complete(exitCode: 0, branchName: null, pullRequestUrl: null, resultSummary: summary);
        dbContext.Set<WorkerRun>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenActiveIssueHasTwoRuns_RunStatsContainsSums()
    {
        // Arrange
        IssueId issueId = await SeedQueuedIssueAsync(issueNumber: 1);
        await SeedCompletedRunAsync(issueId, durationMs: 1000, numTurns: 3, totalCostUsd: 0.10m);
        await SeedCompletedRunAsync(issueId, durationMs: 2000, numTurns: 5, totalCostUsd: 0.20m);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content.ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(
            TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        IssueSummary summary = summaries.ShouldHaveSingleItem();
        RunStats runStats = summary.RunStats.ShouldNotBeNull();
        runStats.ShouldSatisfyAllConditions(
            () => runStats.RunCount.ShouldBe(2),
            () => runStats.DurationMs.ShouldBe(3000L),
            () => runStats.NumTurns.ShouldBe(8),
            () => runStats.TotalCostUsd.ShouldBe(0.30m),
            () => runStats.InputTokens.ShouldBe(200L),
            () => runStats.OutputTokens.ShouldBe(100L));
    }

    [Fact]
    public async Task WhenActiveIssueHasNoRuns_RunStatsIsNull()
    {
        // Arrange
        await SeedQueuedIssueAsync(issueNumber: 2);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IReadOnlyList<IssueSummary>? summaries = await response.Content.ReadFromJsonAsync<IReadOnlyList<IssueSummary>>(
            TestContext.Current.CancellationToken);
        summaries.ShouldNotBeNull();
        IssueSummary summary = summaries.ShouldHaveSingleItem();
        summary.RunStats.ShouldBeNull();
    }

    [Fact]
    public async Task WhenResolvedIssueHasTwoRuns_RunStatsContainsSums()
    {
        // Arrange — seed a completed issue (resolved state).
        using IServiceScope seedScope = _factory.Services.CreateScope();
        DbContext seedDb = seedScope.ServiceProvider.GetRequiredService<DbContext>();

        IssueBuilder builder = new IssueBuilder()
            .WithMonitoredRepositoryId(RepositoryId)
            .WithIssueNumber(3)
            .WithTitle("Resolved Issue")
            .WithBody("Body")
            .WithLabels([])
            .WithBranchName("feat/3-fix")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/3")
            .WithFeedbackCutoffAt(DateTimeOffset.UtcNow.AddHours(1))
            .WithCompletedAt(DateTimeOffset.UtcNow.AddHours(2));

        CompletedIssue completedIssue = builder.Completed();

        seedDb.Set<Issue>().Add(completedIssue);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        IssueId issueId = completedIssue.Id;
        await SeedCompletedRunAsync(issueId, durationMs: 3000, numTurns: 4, totalCostUsd: 0.15m);
        await SeedCompletedRunAsync(issueId, durationMs: 7000, numTurns: 6, totalCostUsd: 0.25m);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/issues?states=completed", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        PagedIssues? result = await response.Content.ReadFromJsonAsync<PagedIssues>(
            TestContext.Current.CancellationToken);
        result.ShouldNotBeNull();
        IssueSummary summary = result.Items.ShouldHaveSingleItem();
        RunStats runStats = summary.RunStats.ShouldNotBeNull();
        runStats.ShouldSatisfyAllConditions(
            () => runStats.RunCount.ShouldBe(2),
            () => runStats.DurationMs.ShouldBe(10000L),
            () => runStats.NumTurns.ShouldBe(10));
    }
}
