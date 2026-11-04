using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Modules.Workers.Features.Orchestration;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerDispatchServiceTests;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerOutcomeApplierTests;

public sealed class ApplyAsync : WorkerDispatchServiceTestBase
{
    private const string ContainerId = "container-apply";
    private const string Branch = "feat/1-apply";

    public static TheoryData<string> TerminalOutcomes => ["Completed", "Review", "Unchanged", "ContinuableFailure", "Failure"];

    [Theory]
    [MemberData(nameof(TerminalOutcomes))]
    public async Task WhenOutcomeIsTerminal_TransitionsRunAndStopsAndRemovesContainer(string outcomeName)
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        RecordingOrchestrator orchestrator = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, orchestrator);

        // Act
        await sut.ApplyAsync(BuildOutcome(outcomeName), activeRun, TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext assertDb = CreateDbContext();
        WorkerRun run = await assertDb.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        run.ShouldNotBeOfType<ActiveRun>();
        orchestrator.StopAndRemoveCalls.ShouldBe([ContainerId]);
    }

    [Fact]
    public async Task WhenOutcomeIsIndeterminate_ThrowsWithoutTouchingContainer()
    {
        // Arrange
        SeedActiveRun(ContainerId, Branch);
        RecordingOrchestrator orchestrator = new();
        await using FoundryDbContext db = CreateDbContext();
        ActiveRun activeRun = await db.Set<ActiveRun>().SingleAsync(TestContext.Current.CancellationToken);
        WorkerOutcomeApplier sut = BuildApplier(db, orchestrator);

        // Act
        Func<Task> act = () => sut.ApplyAsync(
            new WorkerOutcome.Indeterminate(new Error("Test", "transient")),
            activeRun,
            TestContext.Current.CancellationToken);

        // Assert
        await act.ShouldThrowAsync<System.Diagnostics.UnreachableException>();
        orchestrator.StopAndRemoveCalls.ShouldBeEmpty();
    }

    private static WorkerOutcomeApplier BuildApplier(FoundryDbContext db, IWorkerOrchestrator orchestrator)
        => new(
            db,
            orchestrator,
            new NullIntegrationEventDispatcher(),
            new NullIntegrationEventProcessor(),
            new NullDomainEventDispatcher(),
            NullLogger<WorkerOutcomeApplier>.Instance);

    private static WorkerOutcome BuildOutcome(string name)
    {
        BranchName branch = BranchName.From(Branch);
        PullRequestUrl pullRequestUrl = PullRequestUrl.From("https://github.com/acme/repo/pull/1");

        return name switch
        {
            "Completed" => new WorkerOutcome.Completed(branch, pullRequestUrl, null),
            "Review" => new WorkerOutcome.Review(branch, pullRequestUrl, null),
            "Unchanged" => new WorkerOutcome.Unchanged(branch, null),
            "ContinuableFailure" => new WorkerOutcome.ContinuableFailure(branch, new FailureReason.TimedOut(), null, null),
            "Failure" => new WorkerOutcome.Failure(new FailureReason.TimedOut(), null, null),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    private sealed class NullDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class RecordingOrchestrator : IWorkerOrchestrator
    {
        private readonly List<string> _stopAndRemoveCalls = [];

        public IReadOnlyList<string> StopAndRemoveCalls => _stopAndRemoveCalls;

        public Task<Result<ContainerId>> StartAsync(WorkerContainerSpec spec, CancellationToken cancellationToken)
            => Task.FromResult(Result<ContainerId>.Fail(new Error("Test", "No dispatch")));

        public Task EnsureCredentialVolumeAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAndRemoveAsync(string containerId, CancellationToken cancellationToken)
        {
            _stopAndRemoveCalls.Add(containerId);
            return Task.CompletedTask;
        }

        public Task<WorkerStatusProbe> GetStatusAsync(string containerId, CancellationToken cancellationToken)
            => Task.FromResult<WorkerStatusProbe>(new WorkerStatusProbe.NotFound());

        public Task<IReadOnlyList<(ContainerId ContainerId, WorkerRunId WorkerRunId)>> ListByLabelAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(ContainerId, WorkerRunId)>>([]);

        public async IAsyncEnumerable<string> StreamLogsAsync(
            string containerId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<string?> GetLogsAsync(string containerId, int tailLines, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

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
