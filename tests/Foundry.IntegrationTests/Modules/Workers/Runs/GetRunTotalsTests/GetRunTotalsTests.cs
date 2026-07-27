using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Runs.GetRunTotalsTests;

public sealed class WhenRunTotalsRequested : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    private static readonly DateTimeOffset WindowStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InWindow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OutOfWindow = new(2025, 12, 15, 0, 0, 0, TimeSpan.Zero);

    public WhenRunTotalsRequested()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedCompletedRunAsync(DateTimeOffset createdAt, RunResultSummary? resultSummary = null)
    {
        // No POST endpoint exists for worker runs — seed directly via DbContext.
        // The factory chain (StartingRun.Begin → Activate → Complete) always uses
        // DateTimeOffset.UtcNow for CreatedAt; override via the EF entry to control
        // the timestamp for date-window filtering tests.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-totals-test"),
            BranchName.From("feat/1-totals-test"),
            MonitoredRepositoryId.New());

        CompletedRun completed = active.Complete(
            exitCode: 0,
            branchName: BranchName.From("feat/1-totals-test"),
            pullRequestUrl: null,
            resultSummary: resultSummary);

        dbContext.Set<WorkerRun>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Back-date CreatedAt to the requested timestamp for window-filtering tests.
        dbContext.Entry(completed).Property(r => r.CreatedAt).CurrentValue = createdAt;
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedFailedRunAsync(DateTimeOffset createdAt, RunResultSummary? resultSummary = null)
    {
        // No POST endpoint for worker runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-failed-totals"),
            BranchName.From("feat/2-totals-failed"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            resultSummary: resultSummary);

        dbContext.Set<WorkerRun>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Back-date CreatedAt to the requested timestamp for window-filtering tests.
        dbContext.Entry(failed).Property(r => r.CreatedAt).CurrentValue = createdAt;
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedStartingRunAsync(DateTimeOffset createdAt)
    {
        // No POST endpoint for worker runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());

        dbContext.Set<WorkerRun>().Add(starting);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Back-date CreatedAt to the requested timestamp for window-filtering tests.
        dbContext.Entry(starting).Property(r => r.CreatedAt).CurrentValue = createdAt;
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedActiveRunAsync(DateTimeOffset createdAt)
    {
        // No POST endpoint for worker runs — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-active-totals"),
            BranchName.From("feat/3-active-totals"),
            MonitoredRepositoryId.New());

        dbContext.Set<WorkerRun>().Add(active);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Back-date CreatedAt to the requested timestamp for window-filtering tests.
        dbContext.Entry(active).Property(r => r.CreatedAt).CurrentValue = createdAt;
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenCompletedAndFailedRunsInWindow_Returns200WithSummedFields()
    {
        // Arrange
        RunResultSummary completedSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 3000L,
            numTurns: 4,
            totalCostUsd: 0.10m,
            inputTokens: 500,
            outputTokens: 800);

        RunResultSummary failedSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: true,
            durationMs: 2000L,
            numTurns: 2,
            totalCostUsd: 0.05m,
            inputTokens: 300,
            outputTokens: 400);

        await SeedCompletedRunAsync(InWindow, completedSummary);
        await SeedFailedRunAsync(InWindow, failedSummary);

        string from = Uri.EscapeDataString(WindowStart.ToString("O"));
        string to = Uri.EscapeDataString(WindowEnd.ToString("O"));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/run-totals?from={from}&to={to}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RunTotals? totals = await response.Content.ReadFromJsonAsync<RunTotals>(
            TestContext.Current.CancellationToken);
        totals.ShouldNotBeNull();
        totals.ShouldSatisfyAllConditions(
            () => totals.RunCount.ShouldBe(2),
            () => totals.DurationMs.ShouldBe(5000L),
            () => totals.NumTurns.ShouldBe(6),
            () => totals.TotalCostUsd.ShouldBe(0.15m),
            () => totals.InputTokens.ShouldBe(800L),
            () => totals.OutputTokens.ShouldBe(1200L));
    }

    [Fact]
    public async Task WhenRunsOutsideWindow_ExcludesOutOfWindowRuns()
    {
        // Arrange
        RunResultSummary inSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 2000L,
            numTurns: 3,
            totalCostUsd: 0.03m,
            inputTokens: 200,
            outputTokens: 300);

        RunResultSummary outSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 9999L,
            numTurns: 99,
            totalCostUsd: 9.99m,
            inputTokens: 9999,
            outputTokens: 9999);

        await SeedCompletedRunAsync(InWindow, inSummary);
        await SeedCompletedRunAsync(OutOfWindow, outSummary);

        string from = Uri.EscapeDataString(WindowStart.ToString("O"));
        string to = Uri.EscapeDataString(WindowEnd.ToString("O"));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/run-totals?from={from}&to={to}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RunTotals? totals = await response.Content.ReadFromJsonAsync<RunTotals>(
            TestContext.Current.CancellationToken);
        totals.ShouldNotBeNull();
        totals.ShouldSatisfyAllConditions(
            () => totals.RunCount.ShouldBe(1),
            () => totals.DurationMs.ShouldBe(2000L),
            () => totals.NumTurns.ShouldBe(3),
            () => totals.TotalCostUsd.ShouldBe(0.03m),
            () => totals.InputTokens.ShouldBe(200L),
            () => totals.OutputTokens.ShouldBe(300L));
    }

    [Fact]
    public async Task WhenActiveRunInWindow_AddsToRunCountButNotSums()
    {
        // Arrange
        RunResultSummary completedSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 1000L,
            numTurns: 2,
            totalCostUsd: 0.02m,
            inputTokens: 100,
            outputTokens: 200);

        await SeedCompletedRunAsync(InWindow, completedSummary);
        await SeedActiveRunAsync(InWindow);

        string from = Uri.EscapeDataString(WindowStart.ToString("O"));
        string to = Uri.EscapeDataString(WindowEnd.ToString("O"));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/run-totals?from={from}&to={to}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RunTotals? totals = await response.Content.ReadFromJsonAsync<RunTotals>(
            TestContext.Current.CancellationToken);
        totals.ShouldNotBeNull();
        totals.ShouldSatisfyAllConditions(
            () => totals.RunCount.ShouldBe(2),
            () => totals.DurationMs.ShouldBe(1000L),
            () => totals.NumTurns.ShouldBe(2),
            () => totals.TotalCostUsd.ShouldBe(0.02m),
            () => totals.InputTokens.ShouldBe(100L),
            () => totals.OutputTokens.ShouldBe(200L));
    }

    [Fact]
    public async Task WhenFromIsAfterTo_Returns400BadRequest()
    {
        // Arrange — inverted range: from is later than to.
        string from = Uri.EscapeDataString(WindowEnd.ToString("O"));
        string to = Uri.EscapeDataString(WindowStart.ToString("O"));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/run-totals?from={from}&to={to}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenFromEqualsTo_Returns400BadRequest()
    {
        // Arrange — degenerate window: from and to are identical.
        string from = Uri.EscapeDataString(WindowStart.ToString("O"));
        string to = Uri.EscapeDataString(WindowStart.ToString("O"));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/run-totals?from={from}&to={to}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenStartingRunInWindow_AddsToRunCountButNotSums()
    {
        // Arrange
        RunResultSummary completedSummary = RunResultSummary.Create(
            resultText: null,
            subtype: null,
            isError: false,
            durationMs: 1000L,
            numTurns: 2,
            totalCostUsd: 0.02m,
            inputTokens: 100,
            outputTokens: 200);

        await SeedCompletedRunAsync(InWindow, completedSummary);
        await SeedStartingRunAsync(InWindow);

        string from = Uri.EscapeDataString(WindowStart.ToString("O"));
        string to = Uri.EscapeDataString(WindowEnd.ToString("O"));

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/run-totals?from={from}&to={to}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        RunTotals? totals = await response.Content.ReadFromJsonAsync<RunTotals>(
            TestContext.Current.CancellationToken);
        totals.ShouldNotBeNull();
        totals.ShouldSatisfyAllConditions(
            () => totals.RunCount.ShouldBe(2),
            () => totals.DurationMs.ShouldBe(1000L),
            () => totals.NumTurns.ShouldBe(2),
            () => totals.TotalCostUsd.ShouldBe(0.02m),
            () => totals.InputTokens.ShouldBe(100L),
            () => totals.OutputTokens.ShouldBe(200L));
    }
}
