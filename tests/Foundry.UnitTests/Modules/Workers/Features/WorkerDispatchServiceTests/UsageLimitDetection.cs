using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Features;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerDispatchServiceTests;

public sealed class UsageLimitDetection : WorkerDispatchServiceTestBase
{
    // Usage-limited output that produces a future reset time
    private const string UsageLimitedFutureOutput =
        """
        Some prior output
        {"terminal_reason":"blocking_limit","result":"resets at 2099-01-01T00:00:00Z"}
        """;

    // Usage-limited output that produces a past reset time
    private const string UsageLimitedPastResetOutput =
        """
        Some prior output
        {"terminal_reason":"blocking_limit","result":"resets at 2000-01-01T00:00:00Z"}
        """;

    private WorkerDispatchService BuildServiceWithParser(
        string? containerLogs,
        WorkerStatus exitedStatus,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        IContainerOutputParser outputParser = new ContainerOutputParser();
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
        settings.UsageLimitResetsAt!.Value.Year.ShouldBe(2099);
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
    public async Task WhenContainerExitsWithUsageLimitedOutputAndPastResetTime_DoesNotSetUsageLimitResetsAt()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-past");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut = BuildServiceWithParser(UsageLimitedPastResetOutput, exitedStatus);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.UsageLimitResetsAt.ShouldBeNull();
    }

    [Fact]
    public async Task WhenContainerExitsWithUsageLimitedOutputAndPastResetTime_DispatchesFailedEventWithIsUsageLimitedRequeue()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-usage-limited-requeue");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            UsageLimitedPastResetOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        WorkerRunFailed failedEvent = dispatcher.Captured
            .OfType<WorkerRunFailed>()
            .ShouldHaveSingleItem();
        failedEvent.IsUsageLimitedRequeue.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenSecondContainerExitsWithLaterResetTime_ExtendsThePause()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-extends-pause");

        string earlierOutput =
            """
            {"terminal_reason":"blocking_limit","result":"resets at 2099-06-01T00:00:00Z"}
            """;

        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        WorkerDispatchService sut1 = BuildServiceWithParser(earlierOutput, exitedStatus);
        await sut1.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Seed a second run — second tick: reconciliation already ran, monitoring sees the new run
        SeedActiveRun("container-later-reset");

        string laterOutput =
            """
            {"terminal_reason":"blocking_limit","result":"resets at 2099-12-31T00:00:00Z"}
            """;

        WorkerDispatchService sut2 = BuildServiceWithParser(laterOutput, exitedStatus);

        // Act
        await sut2.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert — UsageLimitResetsAt extended to December
        await using FoundryDbContext assertDb = CreateDbContext();
        GlobalSettings? settings = await assertDb.Set<GlobalSettings>()
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        settings.ShouldNotBeNull();
        settings.UsageLimitResetsAt.ShouldNotBeNull();
        settings.UsageLimitResetsAt!.Value.Month.ShouldBe(12);
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus exitedStatus, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in usage-limit tests")));

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WorkerStatus?> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatus?>(exitedStatus);

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
