using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
using Foundry.Modules.Monitoring.Features.Rebase;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Rebase.RebaseStepTests;

/// <summary>
/// Tests for the post-run rebase step (ADR 0087): each test drives one path from a rebase-pending issue to a
/// <see cref="RebaseResolved"/> outcome, a wait, or a retry.
/// </summary>
public sealed class RunAsync : IAsyncDisposable
{
    private const string PrUrl = "https://github.com/owner/repo/pull/99";
    private const string PrId = "PR_kwDOabc";
    private const string OldHead = "1111111111111111111111111111111111111111";
    private const string NewHead = "2222222222222222222222222222222222222222";
    private const int IssueNumber = 42;

    private static readonly DateTimeOffset EnteredAt = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = EnteredAt.AddSeconds(5);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly WebhookNudgeQueue _nudgeQueue = new(NullLogger<WebhookNudgeQueue>.Instance);
    private readonly ScriptedProvider _provider = new();

    public RunAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<FoundryDbContext> options = new DbContextOptionsBuilder<FoundryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FoundryDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private MonitoredRepository SeedRepository(bool rebaseAfterRun = true)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null,
            rebaseAfterRun: rebaseAfterRun).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    private RebaseStep BuildSut(params RebasePendingIssueInfo[] pending) =>
        new(
            new StubIssueQueries(pending.Length == 0 ? [new RebasePendingIssueInfo(IssueNumber, PrUrl, EnteredAt)] : pending),
            _dbContext,
            _dispatcher,
            _nudgeQueue,
            NullLogger<RebaseStep>.Instance);

    private RebaseResolved SingleResolved() =>
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldHaveSingleItem();

    private PendingRebase LoadProgress()
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<PendingRebase>().Single();
    }

    private List<Nudge> DrainNudges()
    {
        List<Nudge> nudges = [];
        while (_nudgeQueue.Reader.TryRead(out Nudge nudge))
        {
            nudges.Add(nudge);
        }

        return nudges;
    }

    [Fact]
    public async Task WhenRebaseAfterRunIsOff_ResolvesDisabledWithoutProviderCalls()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository(rebaseAfterRun: false);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        RebaseResolved resolved = SingleResolved();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.RepositoryId.ShouldBe(repository.Id),
            () => resolved.IssueNumber.ShouldBe(IssueNumber),
            () => resolved.Outcome.ShouldBe("disabled"),
            () => resolved.Reason.ShouldBeNull());
        _provider.StateCalls.ShouldBe(0);
        _provider.IsClosedCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenBranchIsNotBehind_ResolvesUpToDateWithoutRebaseCall()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(OldHead)));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("up_to_date");
        _provider.RebaseCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenBehind_RequestsRebaseWithExpectedHeadAndWaitsForNewHead()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.RebaseCalls.ShouldHaveSingleItem().ShouldBe((PrId, OldHead));
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldBeEmpty();
        PendingRebase progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.PreRebaseHeadSha.ShouldBe(OldHead),
            () => progress.RequestedAt.ShouldBe(Now),
            () => progress.IsResolved.ShouldBeFalse());
        Nudge followUp = DrainNudges().ShouldHaveSingleItem();
        followUp.ShouldSatisfyAllConditions(
            () => followUp.Key.Pass.ShouldBe(NudgePass.Rebase),
            () => followUp.Key.IssueNumber.ShouldBe(IssueNumber),
            () => followUp.NotBefore.ShouldBe(Now + PendingRebase.LandingPollDelay));
    }

    [Fact]
    public async Task WhenHeadIsStillThePreRebaseHead_StaysPending()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        RebaseStep sut = BuildSut();
        await sut.RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldBeEmpty();
        _provider.RebaseCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenNewHeadAppears_ResolvesRebased()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(NewHead)));
        RebaseStep sut = BuildSut();
        await sut.RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("rebased");
        LoadProgress().IsResolved.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenNewHeadNotSeenWithinTheBound_ResolvesSkippedTimeout()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        RebaseStep sut = BuildSut();
        await sut.RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await sut.RunAsync(repository, _provider, null, Now + PendingRebase.LandingTimeout, TestContext.Current.CancellationToken);

        // Assert
        RebaseResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(RebaseSkipReasons.Timeout);
    }

    [Fact]
    public async Task WhenProviderReportsConflict_ResolvesConflict()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Conflict()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("conflict");
    }

    [Fact]
    public async Task WhenProviderSaysNoNewCommits_ResolvesUpToDate()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.UpToDate()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("up_to_date");
    }

    [Fact]
    public async Task WhenProviderRefuses_ResolvesSkippedWithTheReason()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(
            new RebaseRequestOutcome.Refused(RebaseSkipReasons.PermissionDenied)));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        RebaseResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(RebaseSkipReasons.PermissionDenied);
    }

    [Fact]
    public async Task WhenProviderIsUnsupported_ResolvesSkippedUnsupported()
    {
        // Arrange — the default IIssueProvider rebase methods answer Unsupported.
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Unsupported()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        RebaseResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(RebaseSkipReasons.Unsupported);
    }

    [Fact]
    public async Task WhenHeadIsStale_RereadsTheHeadAndRetries()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.StaleHead()));
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, NewHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.RebaseCalls.ShouldBe([(PrId, OldHead), (PrId, NewHead)]);
        LoadProgress().PreRebaseHeadSha.ShouldBe(NewHead);
    }

    [Fact]
    public async Task WhenHeadStaysStale_ResolvesSkippedStaleHead()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        for (int i = 0; i < RebaseStep.MaxStaleHeadAttempts; i++)
        {
            _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
            _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.StaleHead()));
        }

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Reason.ShouldBe(RebaseSkipReasons.StaleHead);
    }

    [Fact]
    public async Task WhenProviderFailsTransiently_BacksOffAndNudgesLater()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Fail(new Error("GitHub.UnexpectedStatusCode", "502")));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldBeEmpty();
        PendingRebase progress = LoadProgress();
        progress.TransientFailures.ShouldBe(1);
        progress.NextAttemptAt.ShouldNotBeNull().ShouldBeGreaterThan(Now);
        DrainNudges().ShouldHaveSingleItem().NotBefore.ShouldBe(progress.NextAttemptAt);
    }

    [Fact]
    public async Task WhenBackingOff_DoesNotCallTheProviderBeforeTheNextAttempt()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Fail(new Error("GitHub.UnexpectedStatusCode", "502")));
        RebaseStep sut = BuildSut();
        await sut.RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.StateCalls.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTransientFailuresExhaustTheBudget_ResolvesSkippedProviderError()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RebaseStep sut = BuildSut();
        DateTimeOffset now = Now;

        // Act
        for (int attempt = 0; attempt < PendingRebase.MaxTransientFailures; attempt++)
        {
            _provider.States.Enqueue(Result<PullRequestRebaseState>.Fail(new Error("GitHub.RateLimitExhausted", "rate limited")));
            await sut.RunAsync(repository, _provider, null, now, TestContext.Current.CancellationToken);
            now = now.AddHours(1);
        }

        // Assert
        RebaseResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(RebaseSkipReasons.ProviderError);
    }

    [Fact]
    public async Task WhenHttpRequestThrows_TreatsItAsTransient()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.ThrowOnState = new HttpRequestException("connection reset");

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        LoadProgress().TransientFailures.ShouldBe(1);
    }

    [Fact]
    public async Task WhenProviderReportsRebaseInProgress_StaysPending()
    {
        // Arrange — GitLab: the head is unchanged and rebase_in_progress is still true.
        MonitoredRepository repository = SeedRepository();
        RebaseStep sut = await RequestRebaseAsync(repository);
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.Behind(PrId, OldHead, 3) { Progress = new ProviderRebaseProgress.InProgress() }));

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldBeEmpty();
        LoadProgress().IsResolved.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenProviderReportsRebaseCompletedWithoutError_ResolvesRebased()
    {
        // Arrange — GitLab: rebase_in_progress false, merge_error null.
        MonitoredRepository repository = SeedRepository();
        RebaseStep sut = await RequestRebaseAsync(repository);
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.Behind(PrId, OldHead, 3) { Progress = new ProviderRebaseProgress.Completed() }));

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("rebased");
    }

    [Fact]
    public async Task WhenProviderReportsRebaseConflicted_ResolvesConflict()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RebaseStep sut = await RequestRebaseAsync(repository);
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.Behind(PrId, OldHead, 3) { Progress = new ProviderRebaseProgress.Conflicted() }));

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("conflict");
    }

    [Fact]
    public async Task WhenProviderReportsRebaseFailed_ResolvesSkippedWithTheReason()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RebaseStep sut = await RequestRebaseAsync(repository);
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.Behind(PrId, OldHead, 3)
            {
                Progress = new ProviderRebaseProgress.Failed(RebaseSkipReasons.RebaseFailed),
            }));

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        RebaseResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(RebaseSkipReasons.RebaseFailed);
    }

    [Fact]
    public async Task WhenRebaseStillInProgressAfterTheBound_ResolvesSkippedTimeout()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RebaseStep sut = await RequestRebaseAsync(repository);
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.Behind(PrId, OldHead, 3) { Progress = new ProviderRebaseProgress.InProgress() }));

        // Act
        await sut.RunAsync(repository, _provider, null, Now + PendingRebase.LandingTimeout, TestContext.Current.CancellationToken);

        // Assert
        RebaseResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(RebaseSkipReasons.Timeout);
    }

    [Fact]
    public async Task WhenNewHeadAppearsDespiteAStaleMergeError_ResolvesRebased()
    {
        // Arrange — a merge_error left over from an earlier rebase never outranks an observed new head.
        MonitoredRepository repository = SeedRepository();
        RebaseStep sut = await RequestRebaseAsync(repository);
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.UpToDate(NewHead) { Progress = new ProviderRebaseProgress.Conflicted() }));

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddSeconds(30), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("rebased");
    }

    [Fact]
    public async Task WhenARebaseIsAlreadyRunning_AwaitsItWithoutRequestingAnother()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.Behind(PrId, OldHead, 3) { Progress = new ProviderRebaseProgress.InProgress() }));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.RebaseCalls.ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldBeEmpty();
        PendingRebase progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.PreRebaseHeadSha.ShouldBe(OldHead),
            () => progress.RequestedAt.ShouldBe(Now));
    }

    [Fact]
    public async Task WhenBehindWithAStaleMergeErrorBeforeRequesting_StillRequestsTheRebase()
    {
        // Arrange — before the request, a failure report is left over from an earlier rebase and is ignored.
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(
            new PullRequestRebaseState.Behind(PrId, OldHead, 3) { Progress = new ProviderRebaseProgress.Conflicted() }));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.RebaseCalls.ShouldHaveSingleItem().ShouldBe((PrId, OldHead));
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldBeEmpty();
    }

    private async Task<RebaseStep> RequestRebaseAsync(MonitoredRepository repository)
    {
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));
        RebaseStep sut = BuildSut();
        await sut.RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        return sut;
    }

    [Fact]
    public async Task WhenProgressSurvivesARestart_ResumesWaitingForTheNewHead()
    {
        // Arrange — the first step requested the rebase; a new step instance (a restarted host) picks it up.
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.Behind(PrId, OldHead, 3)));
        _provider.Requests.Enqueue(Result<RebaseRequestOutcome>.Ok(new RebaseRequestOutcome.Requested()));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(NewHead)));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("rebased");
        _provider.RebaseCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenAlreadyResolved_DoesNotRunAgainForTheSameEpisode()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(OldHead)));
        RebaseStep sut = BuildSut();
        await sut.RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await sut.RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved();
        _provider.StateCalls.ShouldBe(1);
    }

    [Fact]
    public async Task WhenIssueReEntersRebasePending_StartsANewEpisode()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(OldHead)));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(NewHead)));

        // Act
        await BuildSut(new RebasePendingIssueInfo(IssueNumber, PrUrl, EnteredAt.AddHours(1)))
            .RunAsync(repository, _provider, null, Now.AddHours(1), TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task WhenPullRequestClosedWithoutMerge_RecordsProviderPullRequestClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: true, IsMerged: false);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        ProviderPullRequestClosed closed = _dispatcher.DispatchedEvents.OfType<ProviderPullRequestClosed>().ShouldHaveSingleItem();
        closed.IssueNumber.ShouldBe(IssueNumber);
        _dispatcher.DispatchedEvents.OfType<RebaseResolved>().ShouldBeEmpty();
        _provider.StateCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenIssueClosed_RecordsProviderIssueClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.IssueClosed = true;

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.OfType<ProviderIssueClosed>().ShouldHaveSingleItem().IssueNumber.ShouldBe(IssueNumber);
    }

    [Fact]
    public async Task WhenPullRequestMerged_RecordsNothing()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.PullRequestStatus = new PullRequestStatus(IsClosed: true, IsMerged: true);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.StateCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenNudgedForAnotherIssue_LeavesThisIssueAlone()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();

        // Act
        await BuildSut().RunAsync(repository, _provider, IssueNumber + 1, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.IsClosedCalls.ShouldBe(0);
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueLeftRebasePending_ForgetsItsProgress()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.States.Enqueue(Result<PullRequestRebaseState>.Ok(new PullRequestRebaseState.UpToDate(OldHead)));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await new RebaseStep(new StubIssueQueries([]), _dbContext, _dispatcher, _nudgeQueue, NullLogger<RebaseStep>.Instance)
            .RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        _dbContext.Set<PendingRebase>().ShouldBeEmpty();
    }

    private sealed class StubIssueQueries(
        IReadOnlyList<RebasePendingIssueInfo> rebasePendingIssues) : IIssueQueries
    {
        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(rebasePendingIssues);

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

        public Task<IssueSummary?> GetIssueSummaryAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult<IssueSummary?>(null);

        public Task<Result<IssueDetail>> GetIssueDetailAsync(
            IssueId issueId,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));

        public Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string>? states,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<PagedIssues> GetResolvedIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string> states,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<IssueStateCounts> GetIssueStateCountsAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }


    private sealed class ScriptedProvider : IIssueProvider
    {
        public Queue<Result<PullRequestRebaseState>> States { get; } = new();

        public Queue<Result<RebaseRequestOutcome>> Requests { get; } = new();

        public List<(string PullRequestId, string ExpectedHeadSha)> RebaseCalls { get; } = [];

        public int StateCalls { get; private set; }

        public int IsClosedCalls { get; private set; }

        public bool IssueClosed { get; set; }

        public PullRequestStatus PullRequestStatus { get; set; } = new(IsClosed: false, IsMerged: false);

        public Exception? ThrowOnState { get; set; }

        public Task<Result<PullRequestRebaseState>> GetRebaseStateAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
        {
            StateCalls++;
            if (ThrowOnState is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(States.Dequeue());
        }

        public Task<Result<RebaseRequestOutcome>> RequestRebaseAsync(
            RepositorySlug slug,
            string pullRequestId,
            string expectedHeadSha,
            CancellationToken cancellationToken)
        {
            RebaseCalls.Add((pullRequestId, expectedHeadSha));
            return Task.FromResult(Requests.Dequeue());
        }

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            IsClosedCalls++;
            return Task.FromResult(Result<bool>.Ok(IssueClosed));
        }

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<PullRequestStatus>.Ok(PullRequestStatus));

        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug,
            int issueNumber,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            DateTimeOffset since,
            PostedCommentIds postedComments,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug,
            string branchName,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Checks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _events = [];

        public IReadOnlyList<IIntegrationEvent> DispatchedEvents => _events;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _events.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
