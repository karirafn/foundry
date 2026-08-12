using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Dispatch;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;

public sealed class UsageLimitDetection : WorkerDispatchServiceTestBase
{
    // Usage-limited output that produces a future reset time
    private const string UsageLimitedFutureOutput =
        """
        Some prior output
        {"terminal_reason":"blocking_limit","result":"resets at 2099-01-01T00:00:00Z"}
        """;

    // 429-only output — no terminal_reason, no parseable reset time.
    // The parser returns CreditsExhausted (step 5 will map this to a CreditsExhausted failure reason).
    private const string Credits429OnlyOutput =
        """
        Some prior output
        {"api_error_status":429}
        """;

    private WorkerDispatchService BuildServiceWithParser(
        string? containerLogs,
        WorkerStatus exitedStatus,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        IContainerOutputParser outputParser = new ContainerOutputParser(NullLogger<ContainerOutputParser>.Instance);
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, containerLogs);

        return base.BuildService(
            orchestrator,
            integrationEventDispatcher: integrationEventDispatcher,
            containerOutputParser: outputParser);
    }

    private void SeedGlobalSettings()
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();
        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    [Fact]
    public async Task WhenContainerExitsWithUsageLimitedOutputAndFutureResetTime_TransitionsToFailedRunWithUsageLimitedReason()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-future");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(UsageLimitedFutureOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.UsageLimited>();
    }

    [Fact]
    public async Task WhenContainerExitsWithUsageLimitedOutputAndFutureResetTime_SetsUsageLimitResetsAtOnGlobalSettings()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-pause");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(UsageLimitedFutureOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.UsageLimitResetsAt.ShouldNotBeNull();
        settings.UsageLimitResetsAt!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        settings.UsageLimitResetsAt!.Value.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddDays(7).AddSeconds(1));
    }

    [Fact]
    public async Task WhenContainerExitsWithUsageLimitedOutputAndFutureResetTime_DispatchesFailedEventWithNullBranchName()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-no-commits");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            UsageLimitedFutureOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContainerExitsWithCreditsExhaustedOutput_TransitionsToFailedRunWithNonZeroExitReason()
    {
        // Arrange — 429-only output (no parseable reset time) now yields CreditsExhausted from the parser.
        // Until step 5 maps CreditsExhausted to its own failure reason, it falls through to NonZeroExit.
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-429-only");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(Credits429OnlyOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — CreditsExhausted is not yet mapped; falls through to NonZeroExit (step 5 will fix)
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.NonZeroExit>();
    }

    [Fact]
    public async Task WhenContainerExitsWithCreditsExhaustedOutput_DoesNotSetUsageLimitResetsAt()
    {
        // Arrange — 429-only output now yields CreditsExhausted; UsageLimitResetsAt must not be set
        // (CreditsExhausted is a distinct pause — step 5 will introduce its own handler).
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-429-sets-resets-at");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(Credits429OnlyOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — CreditsExhausted does not set UsageLimitResetsAt (that is a time-based pause field)
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContainerExitsWithUsageLimitedOutputAndNoGlobalSettingsRow_StillReturnsUsageLimitedReason()
    {
        // Arrange — deliberately NO SeedGlobalSettings() call
        SeedActiveRun("container-no-settings-row");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(UsageLimitedFutureOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — the run must still be marked failed with UsageLimited; no GlobalSettings row must exist
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun? run = await assertDb.Set<WorkerRun>().SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        FailedRun failedRun = run.ShouldBeOfType<FailedRun>();
        failedRun.Reason.ShouldBeOfType<FailureReason.UsageLimited>();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContainerExitsWithUsageLimitedOutput_DispatchesFailedEventWithUsageLimitedCategory()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-category");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            UsageLimitedFutureOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.Category.ShouldBe("usage_limited");
    }

    [Fact]
    public async Task WhenSecondContainerExitsWithLaterResetTime_ExtendsThePause()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-extends-pause");

        DateTimeOffset earlierReset = DateTimeOffset.UtcNow.AddDays(2);
        string earlierOutput = $$$"""{"terminal_reason":"blocking_limit","result":"resets at {{{earlierReset:O}}}"}""";

        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut1 = BuildServiceWithParser(earlierOutput, exitedStatus);
        await sut1.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Seed a second run — second tick: reconciliation already ran, monitoring sees the new run
        SeedActiveRun("container-later-reset");

        DateTimeOffset laterReset = DateTimeOffset.UtcNow.AddDays(5);
        string laterOutput = $$$"""{"terminal_reason":"blocking_limit","result":"resets at {{{laterReset:O}}}"}""";

        WorkerDispatchService sut2 = BuildServiceWithParser(laterOutput, exitedStatus);

        // Act
        await sut2.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — UsageLimitResetsAt extended to the later of the two reset times
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.UsageLimitResetsAt.ShouldNotBeNull();
        settings.UsageLimitResetsAt!.Value.ShouldBeGreaterThanOrEqualTo(laterReset.AddSeconds(-1));
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus exitedStatus, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in usage-limit tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.Available(exitedStatus));

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
            => Task.FromResult(logs);

        public Task StopContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

    }
}
