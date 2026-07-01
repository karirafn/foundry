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

/// <summary>
/// Tests for the DispatchPaused integration event dispatched when a usage limit is newly persisted.
/// </summary>
public sealed class DispatchPausedEventDispatch : WorkerDispatchServiceTestBase
{
    private const string UsageLimitedFutureOutput =
        """
        Some prior output
        {"terminal_reason":"blocking_limit","result":"resets at 2099-01-01T00:00:00Z"}
        """;

    private const string UsageLimitedPastOutput =
        """
        Some prior output
        {"terminal_reason":"blocking_limit","result":"resets at 2000-01-01T00:00:00Z"}
        """;

    private void SeedGlobalSettings(DateTimeOffset? usageLimitResetsAt = null)
    {
        using FoundryDbContext db = CreateDbContext();
        GlobalSettings settings = GlobalSettings.Create();

        if (usageLimitResetsAt.HasValue)
        {
            settings.SetUsageLimitResetsAt(usageLimitResetsAt.Value);
        }

        db.Set<GlobalSettings>().Add(settings);
        db.SaveChanges();
    }

    private WorkerDispatchService BuildServiceWithParser(
        string? containerLogs,
        WorkerStatus exitedStatus,
        IIntegrationEventDispatcher? integrationEventDispatcher = null)
    {
        IContainerOutputParser outputParser = new ContainerOutputParser();
        ExitedWorkerOrchestrator orchestrator = new(exitedStatus, containerLogs);

        return BuildService(
            orchestrator,
            integrationEventDispatcher: integrationEventDispatcher,
            containerOutputParser: outputParser);
    }

    [Fact]
    public async Task WhenUsageLimitIsNewlyPersisted_DispatchesDispatchPausedWithCorrectResetsAt()
    {
        // Arrange
        SeedGlobalSettings();
        SeedActiveRun("container-dispatch-paused-new");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            UsageLimitedFutureOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        DispatchPaused pausedEvent = dispatcher.Captured
            .OfType<DispatchPaused>()
            .ShouldHaveSingleItem();
        pausedEvent.ResetsAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task WhenUsageLimitResetsAtIsInPastAndNoOpOccurs_DoesNotDispatchDispatchPaused()
    {
        // Arrange — past reset time triggers the no-op path in SetUsageLimitResetsAt
        SeedGlobalSettings();
        SeedActiveRun("container-dispatch-paused-past");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            UsageLimitedPastOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.OfType<DispatchPaused>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNewResetTimeShorterThanExisting_DoesNotDispatchDispatchPaused()
    {
        // Arrange — existing reset is longer than the new one; extend-only semantics no-op
        DateTimeOffset longerExistingReset = DateTimeOffset.UtcNow.AddDays(6);
        SeedGlobalSettings(usageLimitResetsAt: longerExistingReset);

        // Force the existing value to persist in DB (SetUsageLimitResetsAt uses UtcNow internally)
        await using (FoundryDbContext db = CreateDbContext())
        {
            GlobalSettings? settings = await db.Set<GlobalSettings>()
                .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
            settings.ShouldNotBeNull();
            // Use EF shadow property to set the value directly, bypassing domain logic
            db.Entry(settings).Property("UsageLimitResetsAt").CurrentValue = longerExistingReset;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        SeedActiveRun("container-dispatch-paused-shorter");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();

        // The output has reset time of 2099 which is ~73 years — but we cap at 7 days.
        // Use a shorter future time to ensure it's less than longerExistingReset.
        DateTimeOffset shorterReset = DateTimeOffset.UtcNow.AddDays(1);
        string shorterOutput =
            $$$"""{"terminal_reason":"blocking_limit","result":"resets at {{{shorterReset:O}}}"}""";

        WorkerDispatchService sut = BuildServiceWithParser(
            shorterOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.OfType<DispatchPaused>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoGlobalSettingsRow_DoesNotDispatchDispatchPaused()
    {
        // Arrange — deliberately NO SeedGlobalSettings() call
        SeedActiveRun("container-dispatch-paused-no-settings");
        WorkerStatus exitedStatus = new(IsRunning: false, ExitCode: 1, FinishedAt: DateTimeOffset.UtcNow);
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerDispatchService sut = BuildServiceWithParser(
            UsageLimitedFutureOutput,
            exitedStatus,
            integrationEventDispatcher: dispatcher);

        // Act
        await sut.ExecuteTickAsync(TestContext.Current.CancellationToken);

        // Assert
        dispatcher.Captured.OfType<DispatchPaused>().ShouldBeEmpty();
    }

    private sealed class ExitedWorkerOrchestrator(WorkerStatus exitedStatus, string? logs) : IWorkerOrchestrator
    {
        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test.NoDispatch", "No dispatch in usage-limit tests")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

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
