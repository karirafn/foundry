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

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Runs.GetWorkerRunLogTests;

public sealed class WhenRunLogRequested : IAsyncDisposable
{
    private const int NotFoundStatus = 404;
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRunLogRequested()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<FailedRun> SeedFailedRunAsync(string? containerOutput = null)
    {
        // No POST endpoint exists for worker runs — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-log-test"),
            BranchName.From("feat/99-log-test"),
            MonitoredRepositoryId.New());

        FailedRun failed = active.Fail(
            new FailureReason.NonZeroExit(1),
            branchNameOrNull: null,
            containerOutput: containerOutput);

        dbContext.Set<WorkerRun>().Add(failed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return failed;
    }

    private async Task<CompletedRun> SeedCompletedRunAsync(string? containerOutput)
    {
        // No POST endpoint exists for worker runs — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-completed-log-test"),
            BranchName.From("feat/101-completed"),
            MonitoredRepositoryId.New());

        CompletedRun completed = active.Complete(
            exitCode: 0,
            branchName: active.BranchName,
            pullRequestUrl: PullRequestUrl.From("https://github.com/owner/repo/pull/101"),
            containerOutput: containerOutput);

        dbContext.Set<WorkerRun>().Add(completed);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return completed;
    }

    private async Task<ActiveRun> SeedActiveRunAsync()
    {
        // No POST endpoint exists for worker runs — seed directly through DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        IssueId issueId = IssueId.New();
        StartingRun starting = StartingRun.Begin(issueId, WorkerRunId.New());
        ActiveRun active = starting.Activate(
            ContainerId.From("container-active-log-test"),
            BranchName.From("feat/100-active"),
            MonitoredRepositoryId.New());

        dbContext.Set<WorkerRun>().Add(active);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return active;
    }

    private async Task SeedTranscriptAsync(WorkerRunId runId, string transcript)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        dbContext.Set<WorkerRunTranscript>().Add(WorkerRunTranscript.Capture(runId, transcript, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenRunHasRunTranscript_Returns200WithTheTranscriptAsStored()
    {
        // Arrange — the stored transcript is raw-looking JSON so re-rendering it would change it
        const string transcript = """2026-11-10T10:00:00.000000000Z {"type":"assistant","message":{"content":[]}}""";
        CompletedRun run = await SeedCompletedRunAsync(containerOutput: "2026-11-10T10:00:00Z tail line\n");
        await SeedTranscriptAsync(run.Id, transcript);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldBe(transcript);
    }

    [Fact]
    public async Task WhenRunDoesNotExist_Returns404AsProblemDetails()
    {
        // Arrange
        Guid unknownId = Guid.NewGuid();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{unknownId}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(NotFoundStatus),
            () => problem.Type.ShouldEndWith("WorkerRun.NotFound"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenFailedRunHasStoredLog_Returns200WithLogContent()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(
            containerOutput: "2024-01-01T00:00:00Z line one\n2024-01-01T00:00:01Z line two\n");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldContain("line one");
        content.ShouldContain("line two");
    }

    [Fact]
    public async Task WhenFailedRunHasStoredStreamJsonLog_ReturnsAgentTranscript()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(
            containerOutput: """
                2026-11-02T17:30:00.000000000Z Cloning into '/workspace'...
                2026-11-02T17:31:30.000000000Z {"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"/workspace/README.md"}}]}}
                2026-11-02T17:31:31.000000000Z {"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"README BODY","is_error":false}]}}
                2026-11-02T17:31:32.000000000Z {"is_error":true,"subtype":"error_max_turns","num_turns":200,"type":"result"}
                """);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Split('\n').ShouldBe(
        [
            "2026-11-02T17:30:00.000000000Z Cloning into '/workspace'...",
            "2026-11-02T17:31:30.000000000Z → Read: /workspace/README.md",
            "2026-11-02T17:31:31.000000000Z ✓ Read (1.0s)",
            "2026-11-02T17:31:32.000000000Z Result: error (error_max_turns) · 200 turns",
        ]);
    }

    [Fact]
    public async Task WhenFailedRunHasNoStoredLog_Returns204()
    {
        // Arrange
        FailedRun run = await SeedFailedRunAsync(containerOutput: null);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task WhenCompletedRunHasStoredLog_Returns200WithTimestampedLines()
    {
        // Arrange
        CompletedRun run = await SeedCompletedRunAsync(
            containerOutput: "2026-11-09T10:00:00.000000000Z PR opened\n2026-11-09T10:20:00.000000000Z exiting\n");

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.ShouldSatisfyAllConditions(
            () => content.ShouldContain("2026-11-09T10:00:00.000000000Z PR opened"),
            () => content.ShouldContain("2026-11-09T10:20:00.000000000Z exiting"));
    }

    [Fact]
    public async Task WhenCompletedRunHasNoStoredLog_Returns204()
    {
        // Arrange
        CompletedRun run = await SeedCompletedRunAsync(containerOutput: null);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task WhenRunIsActive_Returns204()
    {
        // Arrange
        ActiveRun run = await SeedActiveRunAsync();

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri($"/api/workers/runs/{run.Id.Value}/log", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
