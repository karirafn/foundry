using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class RunTranscriptCapture : WorkerDispatchServiceTestBase
{
    private const string FullLog =
        "2026-11-10T10:00:00.000000000Z bootstrap: cloning\n"
        + """2026-11-10T10:00:01.000000000Z {"type":"assistant","message":{"content":[{"type":"text","text":"Starting the work."}]}}""";

    private static readonly WorkerStatus ExitedWithFailure = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);

    private static readonly WorkerStatus Running = new(IsRunning: true, ExitCode: null, FinishedAt: null);

    [Fact]
    public async Task WhenContainerExits_StoresTheRenderedFullLogAsTheRunTranscript()
    {
        // Arrange
        ActiveRun seeded = SeedActiveRun("container-transcript-exited");
        TranscriptStub orchestrator = new(ExitedWithFailure, tail: "tail output", fullLog: FullLog);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunTranscript transcript = (await LoadTranscriptAsync()).ShouldNotBeNull();
        transcript.ShouldSatisfyAllConditions(
            () => transcript.Id.ShouldBe(seeded.Id),
            () => transcript.Transcript.ShouldContain("bootstrap: cloning"),
            () => transcript.Transcript.ShouldContain("Starting the work."),
            () => transcript.Transcript.ShouldNotContain("\"type\":\"assistant\""));
    }

    [Fact]
    public async Task WhenContainerExits_ContainerOutputKeepsTheTail()
    {
        // Arrange
        SeedActiveRun("container-transcript-tail");
        TranscriptStub orchestrator = new(ExitedWithFailure, tail: "tail output", fullLog: FullLog);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failedRun = (await LoadRunAsync()).ShouldBeOfType<FailedRun>();
        failedRun.ContainerOutput.ShouldBe("tail output");
    }

    [Fact]
    public async Task WhenFullLogFetchThrows_StoresNoTranscriptAndStillFailsTheRunWithItsContainerOutput()
    {
        // Arrange
        SeedActiveRun("container-transcript-throws");
        TranscriptStub orchestrator = new(ExitedWithFailure, tail: "tail output", fullLog: null, fullLogThrows: true);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        FailedRun failedRun = (await LoadRunAsync()).ShouldBeOfType<FailedRun>();
        WorkerRunTranscript? transcript = await LoadTranscriptAsync();
        failedRun.ShouldSatisfyAllConditions(
            () => failedRun.ContainerOutput.ShouldBe("tail output"),
            () => transcript.ShouldBeNull());
    }

    [Fact]
    public async Task WhenRunTimesOut_StoresTheRunTranscript()
    {
        // Arrange
        SeedActiveRun("container-transcript-timeout");
        TranscriptStub orchestrator = new(Running, tail: "tail output", fullLog: FullLog);
        DispatchAndPublishTicker sut = BuildService(
            orchestrator,
            settingsQueries: new StubGlobalSettingsQueries(maxConcurrent: 3, timeoutMinutes: 0));

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunTranscript? transcript = await LoadTranscriptAsync();
        transcript.ShouldNotBeNull().Transcript.ShouldContain("Starting the work.");
    }

    [Fact]
    public async Task WhenRunIsCancelled_StoresTheRunTranscript()
    {
        // Arrange
        ActiveRun seeded = SeedActiveRun("container-transcript-cancel");
        await using (FoundryDbContext db = CreateDbContext())
        {
            ActiveRun run = db.Set<ActiveRun>().Single(r => r.Id == seeded.Id);
            run.RequestCancel(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        TranscriptStub orchestrator = new(Running, tail: "tail output", fullLog: FullLog);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunTranscript? transcript = await LoadTranscriptAsync();
        transcript.ShouldNotBeNull().Transcript.ShouldContain("Starting the work.");
    }

    [Fact]
    public async Task WhenContainerIsGone_StoresNoTranscript()
    {
        // Arrange
        SeedActiveRun("container-transcript-gone");
        TranscriptStub orchestrator = new(status: null, tail: null, fullLog: FullLog);
        DispatchAndPublishTicker sut = BuildService(orchestrator);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunTranscript? transcript = await LoadTranscriptAsync();
        transcript.ShouldBeNull();
    }

    private async Task<WorkerRun> LoadRunAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        WorkerRun? run = await db.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        return run.ShouldNotBeNull();
    }

    private async Task<WorkerRunTranscript?> LoadTranscriptAsync()
    {
        await using FoundryDbContext db = CreateDbContext();
        return await db.Set<WorkerRunTranscript>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TranscriptStub(
        WorkerStatus? status,
        string? tail,
        string? fullLog,
        bool fullLogThrows = false) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in transcript tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(status is null
                ? new WorkerStatusProbe.NotFound()
                : new WorkerStatusProbe.Available(status));

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult(tail);

        public Task<string?> GetFullLogAsync(string containerId, CancellationToken cancellationToken)
            => fullLogThrows
                ? throw new InvalidOperationException("Full log fetch failed")
                : Task.FromResult(fullLog);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ReapOrphanedDataRootVolumesAsync(
            IReadOnlySet<WorkerRunId> activeRunIds,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
