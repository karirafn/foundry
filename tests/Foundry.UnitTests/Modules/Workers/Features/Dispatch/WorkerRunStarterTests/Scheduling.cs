using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Publisher;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.WorkerRunStarterTests;

public sealed class Scheduling
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task WhenTwoRunsAreReady_PreparesThemConcurrently()
    {
        // Arrange — the first prepare finishes only once the second has begun
        SecondPrepareGatedPublisher publisher = new();
        await using StarterTestHost host = new(publisher: publisher);
        IssueClaimed first = StarterTestHost.BuildEvent(issueNumber: 1);
        IssueClaimed second = StarterTestHost.BuildEvent(issueNumber: 2);
        await host.ClaimAsync(first, TestContext.Current.CancellationToken);
        await host.ClaimAsync(second, TestContext.Current.CancellationToken);

        // Act
        await host.Starter.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = host.CreateDbContext();
        List<WorkerRun> runs = await db.Set<WorkerRun>().ToListAsync(TestContext.Current.CancellationToken);
        runs.ShouldSatisfyAllConditions(
            () => runs.Count.ShouldBe(2),
            () => runs.ShouldAllBe(run => run is ActiveRun));
    }

    [Fact]
    public async Task WhenAPrepareHasNotReturned_AnotherClaimIsHandled()
    {
        // Arrange
        BlockingPublisher publisher = new();
        await using StarterTestHost host = new(publisher: publisher);
        await host.ClaimAsync(StarterTestHost.BuildEvent(issueNumber: 1), TestContext.Current.CancellationToken);
        Task tick = host.Starter.TickForTest(TestContext.Current.CancellationToken);
        await publisher.Entered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        IssueClaimed second = StarterTestHost.BuildEvent(issueNumber: 2);

        // Act
        await host.ClaimAsync(second, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);

        // Assert
        await using (FoundryDbContext db = host.CreateDbContext())
        {
            WorkerRun secondRun = await db.Set<WorkerRun>()
                .SingleAsync(run => run.Id == second.Dispatch.WorkerRunId, TestContext.Current.CancellationToken);
            secondRun.ShouldBeOfType<StartingRun>();
        }

        publisher.Release.SetResult();
        await tick;
    }

    [Fact]
    public async Task WhenARunIsPrepared_ItsStartAttemptWasCommittedFirst()
    {
        // Arrange
        ObservingPublisher publisher = new();
        await using StarterTestHost host = new(publisher: publisher);
        DateTimeOffset? attemptedAtPrepare = null;
        publisher.Observe = async request =>
        {
            await using FoundryDbContext db = host.CreateDbContext();
            StartingRun run = await db.Set<StartingRun>().SingleAsync(candidate => candidate.Id == request.RunId);
            attemptedAtPrepare = run.StartAttemptedAt;
        };
        await host.ClaimAsync(StarterTestHost.BuildEvent(), TestContext.Current.CancellationToken);

        // Act
        await host.Starter.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        attemptedAtPrepare.ShouldNotBeNull();
    }

    [Fact]
    public async Task WhenARunsStartWasAlreadyAttempted_ItIsNeverPickedAgain()
    {
        // Arrange — a start interrupted by a host stop left the run attempted
        FakePublisher publisher = new();
        await using StarterTestHost host = new(publisher: publisher);
        IssueClaimed @event = StarterTestHost.BuildEvent();
        await using (FoundryDbContext db = host.CreateDbContext())
        {
            StartingRun run = StartingRun.Begin(@event.Dispatch.IssueId, @event.Dispatch.WorkerRunId, claim: @event.Dispatch);
            run.BeginStartAttempt(DateTimeOffset.UtcNow);
            db.Set<WorkerRun>().Add(run);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await host.Starter.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext check = host.CreateDbContext();
        WorkerRun stored = await check.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken);
        stored.ShouldSatisfyAllConditions(
            () => stored.ShouldBeOfType<StartingRun>(),
            () => publisher.Requests.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenARunCarriesNoClaim_ItIsIgnored()
    {
        // Arrange — a run written before runs kept their claim
        FakePublisher publisher = new();
        await using StarterTestHost host = new(publisher: publisher);
        await using (FoundryDbContext db = host.CreateDbContext())
        {
            db.Set<WorkerRun>().Add(StartingRun.Begin(IssueId.New(), WorkerRunId.New()));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        await host.Starter.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext check = host.CreateDbContext();
        StartingRun stored = (await check.Set<WorkerRun>().SingleAsync(TestContext.Current.CancellationToken))
            .ShouldBeOfType<StartingRun>();
        stored.ShouldSatisfyAllConditions(
            () => stored.StartAttemptedAt.ShouldBeNull(),
            () => publisher.Requests.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenOneRunsStartThrows_ThatRunStaysStartingAndTheOtherStarts()
    {
        // Arrange
        await using StarterTestHost host = new(issueContentQueries: new ThrowingForIssueContentQueries(issueNumber: 1));
        IssueClaimed throwing = StarterTestHost.BuildEvent(issueNumber: 1);
        IssueClaimed healthy = StarterTestHost.BuildEvent(issueNumber: 2);
        await host.ClaimAsync(throwing, TestContext.Current.CancellationToken);
        await host.ClaimAsync(healthy, TestContext.Current.CancellationToken);

        // Act
        await host.Starter.TickForTest(TestContext.Current.CancellationToken);

        // Assert
        await using FoundryDbContext db = host.CreateDbContext();
        WorkerRun throwingRun = await db.Set<WorkerRun>()
            .SingleAsync(run => run.Id == throwing.Dispatch.WorkerRunId, TestContext.Current.CancellationToken);
        WorkerRun healthyRun = await db.Set<WorkerRun>()
            .SingleAsync(run => run.Id == healthy.Dispatch.WorkerRunId, TestContext.Current.CancellationToken);
        db.ShouldSatisfyAllConditions(
            () => throwingRun.ShouldBeOfType<StartingRun>(),
            () => healthyRun.ShouldBeOfType<ActiveRun>());
    }

    [Fact]
    public async Task WhenAClaimSignalsTheStarter_TheRunStartsWithoutWaitingForTheTick()
    {
        // Arrange
        await using StarterTestHost host = new();
        await host.Starter.StartAsync(TestContext.Current.CancellationToken);
        IssueClaimed @event = StarterTestHost.BuildEvent();

        // Act
        await host.ClaimAsync(@event, TestContext.Current.CancellationToken);

        // Assert — well inside the 10 s tick, so only the signal can have woken the starter
        bool active = await PollAsync(
            async () =>
            {
                await using FoundryDbContext db = host.CreateDbContext();
                return await db.Set<ActiveRun>().AnyAsync(TestContext.Current.CancellationToken);
            },
            TimeSpan.FromSeconds(5));
        await host.Starter.StopAsync(TestContext.Current.CancellationToken);
        active.ShouldBeTrue();
    }

    private static async Task<bool> PollAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static PrepareOutcome Prepared() =>
        new PrepareOutcome.Prepared(new RunBase(FakePublisher.DefaultBranchHead, null), null);

    /// <summary>Holds the first prepare until a second prepare has begun; fails the first if none begins in time.</summary>
    private sealed class SecondPrepareGatedPublisher : PublisherBase
    {
        private readonly TaskCompletionSource _secondBegan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public override async Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await _secondBegan.Task.WaitAsync(Patience, cancellationToken);
            }
            else
            {
                _secondBegan.TrySetResult();
            }

            return Prepared();
        }
    }

    private sealed class BlockingPublisher : PublisherBase
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Prepared();
        }
    }

    private sealed class ObservingPublisher : PublisherBase
    {
        public Func<PrepareRequest, Task> Observe { get; set; } = _ => Task.CompletedTask;

        public override async Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken)
        {
            await Observe(request);
            return Prepared();
        }
    }

    private abstract class PublisherBase : IPublisher
    {
        public abstract Task<PrepareOutcome> PrepareAsync(PrepareRequest request, CancellationToken cancellationToken);

        public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<PublishResult>(new PublishResult.NothingToPublish());

        public Task<HostProbeOutcome> ProbeAsync(HostProbeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<HostProbeOutcome>(new HostProbeOutcome.Reachable());
    }

    private sealed class ThrowingForIssueContentQueries(int issueNumber) : IIssueContentQueries
    {
        private readonly StubIssueContentQueries _inner = new();

        public Task<Result<IssueContent>> GetIssueContentAsync(
            MonitoredRepositoryId repositoryId,
            int requestedIssueNumber,
            CancellationToken cancellationToken) =>
            requestedIssueNumber == issueNumber
                ? throw new InvalidOperationException("Reading the issue threw.")
                : _inner.GetIssueContentAsync(repositoryId, requestedIssueNumber, cancellationToken);

        public Task<Result<string>> GetPullRequestDescriptionAsync(
            MonitoredRepositoryId repositoryId,
            string pullRequestUrl,
            CancellationToken cancellationToken) =>
            _inner.GetPullRequestDescriptionAsync(repositoryId, pullRequestUrl, cancellationToken);
    }
}
