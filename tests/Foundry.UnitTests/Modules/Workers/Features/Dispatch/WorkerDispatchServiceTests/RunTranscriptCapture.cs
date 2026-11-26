using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class RunTranscriptCapture : WorkerDispatchServiceTestBase
{
    private const string FullLog =
        "2026-11-10T10:00:00.000000000Z bootstrap: cloning\n"
        + """2026-11-10T10:00:01.000000000Z {"type":"assistant","message":{"content":[{"type":"text","text":"Starting the work."}]}}""";

    private static readonly WorkerStatus ExitedWithFailure = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);

    // The FOUNDRY_TOOLCHAIN line the entrypoint writes to stderr before claude starts (ADR 0109).
    private const string FullLogWithToolchain =
        "2026-11-10T10:00:00.000000000Z bootstrap: cloning\n"
        + """2026-11-10T10:00:00.500000000Z FOUNDRY_TOOLCHAIN [{"name":"node","version":"22.22.3"},{"name":"csharp-ls","version":"0.28.0"}]""" + "\n"
        + """2026-11-10T10:00:01.000000000Z {"type":"assistant","message":{"content":[{"type":"text","text":"Starting the work."}]}}""";

    private static readonly WorkerStatus ExitedCleanly = new(IsRunning: false, ExitCode: 0, FinishedAt: DateTimeOffset.UtcNow);

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

    [Fact]
    public async Task WhenContainerExits_RecordsTheToolchainItReportedOnTheRun()
    {
        // Arrange
        SeedActiveRun("container-toolchain-exited");
        TranscriptStub orchestrator = new(ExitedWithFailure, tail: "tail output", fullLog: FullLogWithToolchain);
        DispatchAndPublishTicker sut = BuildService(orchestrator, containerOutputParser: RealParser());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await LoadRunAsync();
        run.ShouldBeOfType<FailedRun>();
        run.Toolchain.ShouldNotBeNull().Entries.Select(entry => (entry.Name, entry.Version)).ShouldBe(
            [("node", "22.22.3"), ("csharp-ls", "0.28.0")]);
    }

    [Fact]
    public async Task WhenContainerExitsCleanly_TheRunKeepsTheToolchainThroughPublishing()
    {
        // Arrange
        SeedActiveRun("container-toolchain-publishing");
        TranscriptStub orchestrator = new(ExitedCleanly, tail: "tail output", fullLog: FullLogWithToolchain);
        DispatchAndPublishTicker sut = BuildService(orchestrator, containerOutputParser: RealParser());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await LoadRunAsync();
        run.ShouldBeOfType<CompletedRun>();
        run.Toolchain.ShouldNotBeNull().Entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenTheFullLogHasNoToolchainLine_RecordsNoToolchain()
    {
        // Arrange
        SeedActiveRun("container-toolchain-absent");
        TranscriptStub orchestrator = new(ExitedWithFailure, tail: "tail output", fullLog: FullLog);
        DispatchAndPublishTicker sut = BuildService(orchestrator, containerOutputParser: RealParser());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await LoadRunAsync();
        run.Toolchain.ShouldBeNull();
    }

    // rate_limit_event lines in the shape the CLI emits on a subscription account, with Docker timestamps.
    private const string FullLogWithPlanUsage =
        """2026-11-10T10:00:01.000000000Z {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.54,"resetsAt":1793399400,"status":"allowed"},"seven_day":{"utilization":0.47,"resetsAt":1793527200,"status":"allowed"}}}}""" + "\n"
        + """2026-11-10T10:00:02.000000000Z {"type":"assistant","message":{"content":[{"type":"text","text":"Working."}]}}""" + "\n"
        + """2026-11-10T10:30:00.000000000Z {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","rateLimitType":"five_hour","unifiedWindows":{"five_hour":{"utilization":0.61,"resetsAt":1793399400,"status":"allowed"}}}}""";

    [Fact]
    public async Task WhenContainerExitsCleanly_TheRunKeepsThePlanUsageOfItsFullLogThroughPublishing()
    {
        // Arrange
        SeedActiveRun("container-plan-usage");
        TranscriptStub orchestrator = new(ExitedCleanly, tail: "tail output", fullLog: FullLogWithPlanUsage);
        DispatchAndPublishTicker sut = BuildService(orchestrator, containerOutputParser: RealParser());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await LoadRunAsync();
        run.ShouldBeOfType<CompletedRun>();
        run.PlanUsageAtStart.ShouldBe(new PlanUsage(0.54m, 0.47m));
        run.PlanUsageAtEnd.ShouldBe(new PlanUsage(0.61m, null));
    }

    [Fact]
    public async Task WhenTheFullLogHasNoRateLimitEvent_RecordsNoPlanUsage()
    {
        // Arrange
        SeedActiveRun("container-plan-usage-absent");
        TranscriptStub orchestrator = new(ExitedWithFailure, tail: "tail output", fullLog: FullLog);
        DispatchAndPublishTicker sut = BuildService(orchestrator, containerOutputParser: RealParser());

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRun run = await LoadRunAsync();
        (run.PlanUsageAtStart, run.PlanUsageAtEnd).ShouldBe(((PlanUsage?)null, (PlanUsage?)null));
    }

    private static ContainerOutputParser RealParser() => new(NullLogger<ContainerOutputParser>.Instance, new PlanUsageWindowReader());

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
