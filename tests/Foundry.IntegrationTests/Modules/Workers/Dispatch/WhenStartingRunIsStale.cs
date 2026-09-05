using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.Shared.Infrastructure.Outbox;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Workers.Dispatch;

// End-to-end integration test for AC 6 and AC 7:
// A stale StartingRun is failed by the sweep → WorkerRunFailed is delivered via the outbox →
// the Issues module transitions InProgressIssue → FailedIssue → POST /retry requeues the issue.
public sealed class WhenStartingRunIsStale : IAsyncDisposable
{
    // Seed CreatedAt old enough to exceed the 10-minute staleness threshold.
    private static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(15);

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenStartingRunIsStale()
    {
        // Replace the Docker-backed IWorkerOrchestrator with a no-op stub that returns no containers —
        // the sweep must not attempt a Docker connection in the test environment.
        // Re-register StaleStartingRunService as a plain singleton so tests can invoke TickForTest
        // without starting the background timer loop (same pattern as TransientRetryService).
        _factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            services.RemoveAll<IWorkerOrchestrator>();
            services.AddSingleton<IWorkerOrchestrator>(new NoContainerOrchestrator());

            services.AddSingleton(sp => new StaleStartingRunService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StaleStartingRunService>.Instance));
        });

        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Seeds an InProgressIssue whose WorkerRunId matches a StartingRun that is back-dated
    /// past the staleness threshold. Returns both the issue and the run.
    /// </summary>
    private async Task<(InProgressIssue Issue, StartingRun Run)> SeedStaleScenarioAsync()
    {
        // Use DbContext directly — no HTTP endpoint produces these states.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();

        // Seed InProgressIssue with the same WorkerRunId as the StartingRun.
        // WorkerRunFailedHandler checks @event.WorkerRunId == inProgress.WorkerRunId, so they must match.
        InProgressIssue inProgress = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithIssueNumber(1)
            .WithTitle("Stale starting-run integration test issue")
            .WithLabels(["foundry"])
            .WithDetectedAt(DateTimeOffset.UtcNow.AddHours(-1))
            .WithWorkerRunId(workerRunId)
            .InProgress();

        dbContext.Set<Issue>().Add(inProgress);

        // Seed StartingRun with the same WorkerRunId.
        StartingRun starting = StartingRun.Begin(inProgress.Id, workerRunId);
        dbContext.Set<WorkerRun>().Add(starting);

        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Back-date created_at via raw SQL — StartingRun.CreatedAt has a private setter.
        DateTimeOffset staleCreatedAt = DateTimeOffset.UtcNow - StaleAge;
        await dbContext.Database.ExecuteSqlAsync(
            $"UPDATE worker_runs SET created_at = {staleCreatedAt:O} WHERE id = {starting.Id.Value}",
            TestContext.Current.CancellationToken);

        return (inProgress, starting);
    }

    [Fact]
    public async Task WhenSweepFails_OutboxDelivers_IssueTransitionsToFailed()
    {
        // Arrange
        (InProgressIssue inProgress, StartingRun _) = await SeedStaleScenarioAsync();

        StaleStartingRunService sweep = _factory.Services.GetRequiredService<StaleStartingRunService>();
        OutboxRelayService relay = _factory.Services.GetRequiredService<OutboxRelayService>();

        // Act — tick the sweep: detects the stale run, fails it, raises WorkerRunFailed domain event,
        // bridge handler enqueues it into the outbox within the same transaction.
        await sweep.TickForTest(TestContext.Current.CancellationToken);

        // Act — tick the relay: delivers the WorkerRunFailed integration event to WorkerRunFailedHandler,
        // which transitions InProgressIssue → FailedIssue.
        await relay.TickForTest(TestContext.Current.CancellationToken);

        // Assert — issue is now FailedIssue
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        Issue? persisted = await dbContext.Set<Issue>()
            .FirstOrDefaultAsync(i => i.Id == inProgress.Id, TestContext.Current.CancellationToken);
        persisted.ShouldBeOfType<FailedIssue>();
    }

    [Fact]
    public async Task WhenIssueIsFailedAfterSweep_RetryEndpointRequeuesIt()
    {
        // Arrange
        (InProgressIssue inProgress, StartingRun _) = await SeedStaleScenarioAsync();

        StaleStartingRunService sweep = _factory.Services.GetRequiredService<StaleStartingRunService>();
        OutboxRelayService relay = _factory.Services.GetRequiredService<OutboxRelayService>();

        // Tick sweep + relay to land the issue in FailedIssue state.
        await sweep.TickForTest(TestContext.Current.CancellationToken);
        await relay.TickForTest(TestContext.Current.CancellationToken);

        // Act — POST /api/issues/{id}/retry to requeue the failed issue.
        HttpResponseMessage response = await _client.PostAsync(
            new Uri($"/api/issues/{inProgress.Id.Value}/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert — HTTP 200 and the issue transitions to queued.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        IssueDetail? detail = await response.Content.ReadFromJsonAsync<IssueDetail>(
            TestContext.Current.CancellationToken);
        detail.ShouldNotBeNull();
        detail.State.ShouldBe("queued");
    }

    /// <summary>
    /// A no-op implementation of <see cref="IWorkerOrchestrator"/> that returns no containers.
    /// Used to prevent the stale-run sweep from attempting a Docker connection in the test environment.
    /// </summary>
    private sealed class NoContainerOrchestrator : IWorkerOrchestrator
    {
        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<Result<ContainerId>> StartAsync(
            WorkerContainerSpec spec,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in integration tests")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string?> GetLogsAsync(
            string containerId,
            int tailLines,
            CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
