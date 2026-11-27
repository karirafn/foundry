using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Merge;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Merge.MergeStepTests;

/// <summary>
/// Tests for the merge step (ADR 0105): each test drives one path from a merge-pending issue to a
/// <see cref="MergeResolved"/> outcome, a close event, or a retry.
/// </summary>
public sealed class RunAsync : IAsyncDisposable
{
    private const string PrUrl = "https://github.com/owner/repo/pull/99";
    private const string VerifiedHead = "1111111111111111111111111111111111111111";
    private const int IssueNumber = 42;
    private const string Branch = "feat/42-thing";

    private static readonly DateTimeOffset EnteredAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FeedbackCutoff = EnteredAt.AddMinutes(-30);
    private static readonly DateTimeOffset Now = EnteredAt.AddSeconds(5);

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly CapturingIntegrationEventDispatcher _dispatcher = new();
    private readonly CapturingIntegrationEventProcessor _processor = new();
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

    private MonitoredRepository SeedRepository(bool mergeWhenChecksPass = true, bool rebaseAfterRun = true)
    {
        MonitoredRepository repository = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "github.com",
            null,
            rebaseAfterRun: rebaseAfterRun,
            mergeWhenChecksPass: mergeWhenChecksPass).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repository);
        _dbContext.SaveChanges();
        return repository;
    }

    private MergeStep BuildSut(params MergePendingIssueInfo[] pending) =>
        BuildQueueSut([], pending.Length == 0 ? [Pending()] : pending);

    /// <summary>
    /// A merge step reading <paramref name="pending"/> as the merge-pending issues and <paramref name="queueIssues"/>
    /// plus every merge-pending issue as the catch-up queue's issues.
    /// </summary>
    private MergeStep BuildQueueSut(IReadOnlyList<MergeQueueIssueInfo> queueIssues, params MergePendingIssueInfo[] pending) =>
        new(
            new StubIssueQueries(
                pending,
                [.. queueIssues, .. pending.Select(p => new MergeQueueIssueInfo(p.IssueNumber, p.BranchName, false, false))]),
            _dbContext,
            new PostedCommentRegistry(_dbContext),
            _dispatcher,
            _processor,
            _nudgeQueue,
            NullLogger<MergeStep>.Instance);

    private MergeResolved SingleResolved() =>
        _dispatcher.DispatchedEvents.OfType<MergeResolved>().ShouldHaveSingleItem();

    private PendingMerge LoadProgress()
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<PendingMerge>().Single();
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
    public async Task WhenSettingIsOff_ResolvesDisabledWithoutProviderCalls()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository(mergeWhenChecksPass: false);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.RepositoryId.ShouldBe(repository.Id),
            () => resolved.IssueNumber.ShouldBe(IssueNumber),
            () => resolved.Outcome.ShouldBe("disabled"),
            () => resolved.Reason.ShouldBeNull());
        _provider.IsClosedCalls.ShouldBe(0);
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenProviderMerges_ResolvesMergedPinnedToTheVerifiedHead()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("merged");
        resolved.Reason.ShouldBeNull();
        _provider.MergeCalls.ShouldHaveSingleItem().ShouldBe((PrUrl, VerifiedHead));
        LoadProgress().IsResolved.ShouldBeTrue();
    }

    // MergeRequestOutcome is internal, so the theory names each provider answer and builds it here.
    private static MergeRequestOutcome Refusal(string variant) =>
        variant switch
        {
            nameof(MergeRequestOutcome.HeadMoved) => new MergeRequestOutcome.HeadMoved(),
            nameof(MergeRequestOutcome.NotMergeable) => new MergeRequestOutcome.NotMergeable("Pull Request is not mergeable"),
            nameof(MergeRequestOutcome.Unauthorized) => new MergeRequestOutcome.Unauthorized(),
            nameof(MergeRequestOutcome.Unsupported) => new MergeRequestOutcome.Unsupported(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };

    [Theory]
    [InlineData(nameof(MergeRequestOutcome.HeadMoved), "head_moved")]
    [InlineData(nameof(MergeRequestOutcome.NotMergeable), "not_mergeable")]
    [InlineData(nameof(MergeRequestOutcome.Unauthorized), "unauthorized")]
    [InlineData(nameof(MergeRequestOutcome.Unsupported), "not_mergeable")]
    public async Task WhenProviderRefuses_ResolvesSkippedWithReason(string variant, string expectedReason)
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(Refusal(variant)));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe(expectedReason);
    }

    [Fact]
    public async Task WhenNoFeedback_ReadsFeedbackSinceTheCutoffThenMerges()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.FeedbackReads.ShouldHaveSingleItem().ShouldBe((PrUrl, FeedbackCutoff));
        _provider.MergeCalls.ShouldHaveSingleItem();
        SingleResolved().Outcome.ShouldBe("merged");
    }

    [Fact]
    public async Task WhenPersonLeftFeedback_ResolvesFeedbackWithTheCommentsWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset newest = Now.AddMinutes(-3);
        ReviewComment comment = new("Please rename this.", "src/a.cs", 3);
        _provider.Feedback = Result<ReviewFeedback>.Ok(new ReviewFeedback([comment], 2, newest));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("feedback"),
            () => resolved.Reason.ShouldBeNull(),
            () => resolved.Comments.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(comment),
            () => resolved.OmittedCommentCount.ShouldBe(2),
            () => resolved.NewestCommentAt.ShouldBe(newest));
        _provider.MergeCalls.ShouldBeEmpty();
        LoadProgress().IsResolved.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenFeedbackIsHeldBack_WaitsForItWithoutMergingOrCountingAFailure()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset heldBackUntil = Now.AddMinutes(2);
        _provider.Feedback = Result<ReviewFeedback>.Ok(new ReviewFeedback([], 0, null, heldBackUntil));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.MergeCalls.ShouldBeEmpty();
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.TransientFailures.ShouldBe(0),
            () => progress.IsResolved.ShouldBeFalse());
        Nudge nudge = DrainNudges().ShouldHaveSingleItem();
        nudge.ShouldSatisfyAllConditions(
            () => nudge.Key.Pass.ShouldBe(NudgePass.Merge),
            () => nudge.Key.IssueNumber.ShouldBe(IssueNumber),
            () => nudge.NotBefore.ShouldBe(heldBackUntil));
    }

    [Fact]
    public async Task WhenFeedbackHasCommentsAndMoreHeldBack_TheCommentsWin()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Feedback = Result<ReviewFeedback>.Ok(
            new ReviewFeedback([new ReviewComment("Please rename this.")], 0, Now.AddMinutes(-3), Now.AddMinutes(2)));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("feedback");
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenFeedbackReadFailsTransiently_CountsAgainstTheMergeBudgetWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Feedback = Result<ReviewFeedback>.Fail(new Error("GitHub.ServerError", "502 Bad Gateway"));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.MergeCalls.ShouldBeEmpty();
        LoadProgress().TransientFailures.ShouldBe(1);
        DrainNudges().ShouldHaveSingleItem().Key.Pass.ShouldBe(NudgePass.Merge);
    }

    [Fact]
    public async Task WhenSettingIsOff_DoesNotReadFeedback()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository(mergeWhenChecksPass: false);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.FeedbackReads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenIssueClosed_RecordsProviderIssueClosedWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.IssueClosed = true;

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        ProviderIssueClosed closed = _dispatcher.DispatchedEvents.ShouldHaveSingleItem().ShouldBeOfType<ProviderIssueClosed>();
        closed.IssueNumber.ShouldBe(IssueNumber);
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPullRequestMergedElsewhere_ResolvesMergedWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.PullRequestState = Result<PullRequestState>.Ok(new PullRequestState.Merged());

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("merged");
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPullRequestClosedWithoutMerge_RecordsProviderPullRequestClosed()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.PullRequestState = Result<PullRequestState>.Ok(new PullRequestState.Closed());

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldHaveSingleItem().ShouldBeOfType<ProviderPullRequestClosed>()
            .IssueNumber.ShouldBe(IssueNumber);
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenPullRequestReadFails_CountsATransientFailureWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.PullRequestState = Result<PullRequestState>.Fail(PullRequestStateErrors.MissingHead());

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.MergeCalls.ShouldBeEmpty();
        _provider.FeedbackReads.ShouldBeEmpty();
        LoadProgress().TransientFailures.ShouldBe(1);
    }

    [Fact]
    public async Task WhenMergeFailsTransiently_CountsTheFailureAndRetriesOnANotBeforeNudge()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Fail(new Error("GitHub.ServerError", "502 Bad Gateway")));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.TransientFailures.ShouldBe(1),
            () => progress.IsResolved.ShouldBeFalse(),
            () => progress.NextAttemptAt.ShouldNotBeNull().ShouldBeGreaterThan(Now));
        Nudge nudge = DrainNudges().ShouldHaveSingleItem();
        nudge.ShouldSatisfyAllConditions(
            () => nudge.Key.Pass.ShouldBe(NudgePass.Merge),
            () => nudge.Key.IssueNumber.ShouldBe(IssueNumber),
            () => nudge.NotBefore.ShouldBe(progress.NextAttemptAt));
    }

    [Fact]
    public async Task WhenMergeThrowsHttpRequestException_CountsATransientFailure()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.ThrowOnMerge = new HttpRequestException("connection reset");

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        LoadProgress().TransientFailures.ShouldBe(1);
    }

    [Fact]
    public async Task WhenTransientFailuresReachTheBudget_ResolvesSkippedProviderError()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        DateTimeOffset now = Now;
        for (int attempt = 0; attempt < PendingMerge.MaxTransientFailures; attempt++)
        {
            _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Fail(new Error("GitHub.ServerError", "502 Bad Gateway")));
        }

        // Act
        for (int attempt = 0; attempt < PendingMerge.MaxTransientFailures; attempt++)
        {
            await BuildSut().RunAsync(repository, _provider, null, now, TestContext.Current.CancellationToken);
            now = now.AddHours(1);
        }

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe("provider_error");
        _provider.MergeCalls.Count.ShouldBe(PendingMerge.MaxTransientFailures);
    }

    [Fact]
    public async Task WhenNotYetDue_SchedulesNudgeWithoutProviderCalls()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Fail(new Error("GitHub.ServerError", "502 Bad Gateway")));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        DrainNudges();
        int isClosedCalls = _provider.IsClosedCalls;

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddSeconds(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.IsClosedCalls.ShouldBe(isClosedCalls);
        DrainNudges().ShouldHaveSingleItem().Key.Pass.ShouldBe(NudgePass.Merge);
    }

    [Fact]
    public async Task WhenAlreadyResolved_DoesNotMergeAgain()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.MergeCalls.ShouldHaveSingleItem();
        SingleResolved();
    }

    [Fact]
    public async Task WhenNewEpisodeStarts_ResetsTheProgress()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await BuildSut(new MergePendingIssueInfo(IssueNumber, PrUrl, Branch, VerifiedHead, FeedbackCutoff, EnteredAt.AddHours(1), IsPullRequestMarkedReady: false))
            .RunAsync(repository, _provider, null, Now.AddHours(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.MergeCalls.Count.ShouldBe(2);
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
    public async Task WhenIssueLeftMergePending_ForgetsItsProgress()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await new MergeStep(new StubIssueQueries([], []), _dbContext, new PostedCommentRegistry(_dbContext), _dispatcher, _processor, _nudgeQueue, NullLogger<MergeStep>.Instance)
            .RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _dbContext.ChangeTracker.Clear();
        _dbContext.Set<PendingMerge>().ShouldBeEmpty();
    }

    private static MergePendingIssueInfo Pending(
        int issueNumber = IssueNumber,
        string branch = Branch,
        DateTimeOffset? enteredAt = null,
        bool isMarkedReady = true,
        string prUrl = PrUrl) =>
        new(issueNumber, prUrl, branch, VerifiedHead, FeedbackCutoff, enteredAt ?? EnteredAt, isMarkedReady);

    private void RefuseBehind(string branch = Branch)
    {
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.NotMergeable("behind")));
        _provider.Ancestry[branch] = Result<bool>.Ok(false);
    }

    private PendingMerge LoadProgress(int issueNumber)
    {
        _dbContext.ChangeTracker.Clear();
        return _dbContext.Set<PendingMerge>().Single(m => m.IssueNumber == issueNumber);
    }

    /// <summary>Seeds a row that has already used <paramref name="rounds"/> catch-ups and is back in merge-pending.</summary>
    private void SeedCaughtUpRow(MonitoredRepository repository, int rounds, DateTimeOffset enteredAt)
    {
        PendingMerge row = PendingMerge.Start(repository.Id, IssueNumber, EnteredAt.AddDays(-1));
        for (int round = 0; round < rounds; round++)
        {
            row.AwaitCatchUp(EnteredAt.AddDays(-1));
            row.StartCatchUp(EnteredAt.AddDays(-1));
            row.Restart(EnteredAt.AddDays(-1).AddHours(round + 1));
        }

        row.Restart(enteredAt);
        _dbContext.Set<PendingMerge>().Add(row);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task WhenRefusedAndBehind_QueuesTheRowAndStaysMergePending()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.AncestryReads.ShouldHaveSingleItem().ShouldBe(("HEAD", Branch));
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.QueuedAt.ShouldBe(Now),
            () => progress.AwaitsCatchUp.ShouldBeTrue(),
            () => progress.CatchUpRounds.ShouldBe(0),
            () => progress.IsResolved.ShouldBeFalse(),
            () => progress.TransientFailures.ShouldBe(0));
    }

    [Fact]
    public async Task WhenRefusedAndBehind_NudgesTheNewQueueHead()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        Nudge nudge = DrainNudges().ShouldHaveSingleItem();
        nudge.ShouldSatisfyAllConditions(
            () => nudge.Key.Pass.ShouldBe(NudgePass.Merge),
            () => nudge.Key.IssueNumber.ShouldBe(IssueNumber),
            () => nudge.NotBefore.ShouldBeNull());
    }

    [Fact]
    public async Task WhenRefusedAndUpToDate_ResolvesSkippedNotMergeable()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.NotMergeable("approvals")));
        _provider.Ancestry[Branch] = Result<bool>.Ok(true);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe("not_mergeable");
        LoadProgress().IsQueued.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenRefusedWithRebaseAfterRunOff_ResolvesNotMergeableWithoutAnAncestryRead()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository(rebaseAfterRun: false);
        RefuseBehind();

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Reason.ShouldBe("not_mergeable");
        _provider.AncestryReads.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAncestryReadFailsTransiently_CountsAgainstTheMergeBudget()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.NotMergeable("behind")));
        _provider.Ancestry[Branch] = Result<bool>.Fail(RunPublishingErrors.Transient("502 Bad Gateway"));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.TransientFailures.ShouldBe(1),
            () => progress.IsQueued.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenAncestryReadFailsPermanently_ResolvesNotMergeable()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.NotMergeable("behind")));
        _provider.Ancestry[Branch] = Result<bool>.Fail(RunPublishingErrors.Permanent("404"));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Reason.ShouldBe("not_mergeable");
    }

    [Fact]
    public async Task WhenQueueHeadHasNothingToWaitFor_StartsACatchUpWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("catch_up"),
            () => resolved.Reason.ShouldBeNull(),
            () => resolved.IssueNumber.ShouldBe(IssueNumber));
        _provider.MergeCalls.ShouldHaveSingleItem();
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.CatchUpRounds.ShouldBe(1),
            () => progress.QueuedAt.ShouldBe(Now),
            () => progress.AwaitsCatchUp.ShouldBeFalse(),
            () => progress.IsResolved.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenQueueHeadMovedSinceItsChecks_ResolvesSkippedHeadMoved()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.PullRequestState = Result<PullRequestState>.Ok(new PullRequestState.Open(CommitSha.From(new string('2', 40)), IsDraft: false));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe("head_moved");
        LoadProgress().IsQueued.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenQueuedBehindAnEarlierEntry_WaitsWithoutMergingButReadsFeedback()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        MergePendingIssueInfo first = Pending(41, "feat/41-first");
        MergePendingIssueInfo second = Pending(42, "feat/42-second");
        RefuseBehind(first.BranchName);
        await BuildQueueSut([], first).RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        RefuseBehind(second.BranchName);
        await BuildQueueSut([], first, second).RunAsync(repository, _provider, 42, Now.AddMinutes(1), TestContext.Current.CancellationToken);
        DrainNudges();
        int merges = _provider.MergeCalls.Count;
        int feedbackReads = _provider.FeedbackReads.Count;

        // Act: the earlier entry is catching up; the later one waits.
        await BuildQueueSut([new MergeQueueIssueInfo(41, first.BranchName, true, false)], second)
            .RunAsync(repository, _provider, null, Now.AddMinutes(2), TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.MergeCalls.Count.ShouldBe(merges);
        _provider.FeedbackReads.Count.ShouldBe(feedbackReads + 1);
        LoadProgress(42).AwaitsCatchUp.ShouldBeTrue();
        LoadProgress(41).IsQueued.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTwoEntriesQueueInOnePass_TheLowerIssueNumberHeadsTheQueue()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        MergePendingIssueInfo first = Pending(41, "feat/41-first");
        MergePendingIssueInfo second = Pending(42, "feat/42-second");
        RefuseBehind(first.BranchName);
        RefuseBehind(second.BranchName);
        await BuildQueueSut([], first, second).RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await BuildQueueSut([], first, second).RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.IssueNumber.ShouldBe(41);
        resolved.Outcome.ShouldBe("catch_up");
    }

    [Fact]
    public async Task WhenAnotherAutoMergeIssueIsUpToDate_TheHeadWaits()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.Ancestry["feat/50-fresh"] = Result<bool>.Ok(true);

        // Act
        await BuildQueueSut([new MergeQueueIssueInfo(50, "feat/50-fresh", false, false)], Pending())
            .RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.AncestryReads.ShouldContain(("HEAD", "feat/50-fresh"));
        LoadProgress().AwaitsCatchUp.ShouldBeTrue();
    }

    public static TheoryData<string, bool, Result<bool>> NonHoldingIssues => new()
    {
        { "behind", false, Result<bool>.Ok(false) },
        { "read fails", false, Result<bool>.Fail(RunPublishingErrors.Transient("502")) },
        { "person feedback", true, Result<bool>.Ok(true) },
    };

    [Theory]
    [MemberData(nameof(NonHoldingIssues))]
    public async Task WhenNoOtherIssueIsConfirmedUpToDate_TheHeadStartsItsCatchUp(
        string scenario,
        bool hasPersonFeedback,
        Result<bool> ancestry)
    {
        // Arrange
        _ = scenario;
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.Ancestry["feat/50-other"] = ancestry;

        // Act
        await BuildQueueSut([new MergeQueueIssueInfo(50, "feat/50-other", false, hasPersonFeedback)], Pending())
            .RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("catch_up");
    }

    [Fact]
    public async Task WhenBackFromACatchUp_AttemptsTheMergeAgainKeepingItsPlace()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildSut(Pending(enteredAt: EnteredAt.AddHours(1)))
            .RunAsync(repository, _provider, null, Now.AddHours(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.MergeCalls.Count.ShouldBe(2);
        _dispatcher.DispatchedEvents.OfType<MergeResolved>().Select(e => e.Outcome).ShouldBe(["catch_up", "merged"]);
        LoadProgress().IsQueued.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenBackFromACatchUpAndBehindAgain_WaitsForAnotherRound()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        SeedCaughtUpRow(repository, rounds: 1, EnteredAt);
        RefuseBehind();

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.AwaitsCatchUp.ShouldBeTrue(),
            () => progress.CatchUpRounds.ShouldBe(1),
            () => progress.QueuedAt.ShouldBe(EnteredAt.AddDays(-1)));
    }

    [Fact]
    public async Task WhenEveryCatchUpRoundIsUsedAndBehindAgain_ResolvesSkippedFellBehind()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        SeedCaughtUpRow(repository, MergeQueue.MaxCatchUpRounds, EnteredAt);
        RefuseBehind();

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe("fell_behind");
        LoadProgress().IsQueued.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAMergeLands_NudgesTheQueueHead()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        MergePendingIssueInfo queued = Pending(41, "feat/41-queued");
        MergePendingIssueInfo merging = Pending(42, "feat/42-merging");
        RefuseBehind(queued.BranchName);
        await BuildQueueSut([], queued).RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.Ancestry[merging.BranchName] = Result<bool>.Ok(true);
        await BuildQueueSut([], queued, merging).RunAsync(repository, _provider, 41, Now.AddMinutes(1), TestContext.Current.CancellationToken);
        DrainNudges();
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildQueueSut([], queued, merging).RunAsync(repository, _provider, 42, Now.AddMinutes(2), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("merged");
        Nudge nudge = DrainNudges().ShouldHaveSingleItem();
        nudge.ShouldSatisfyAllConditions(
            () => nudge.Key.IssueNumber.ShouldBe(41),
            () => nudge.NotBefore.ShouldBeNull());
    }

    [Fact]
    public async Task WhenQueuedIssueIsCatchingUp_KeepsItsRow()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Act: the issue left merge-pending for its catch-up rebase.
        await BuildQueueSut([new MergeQueueIssueInfo(IssueNumber, Branch, true, false)])
            .RunAsync(repository, _provider, null, Now.AddMinutes(2), TestContext.Current.CancellationToken);

        // Assert
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.QueuedAt.ShouldBe(Now),
            () => progress.CatchUpRounds.ShouldBe(1));
    }

    [Fact]
    public async Task WhenQueuedIssueLeavesTheMergePath_ForgetsItsRowAndARejoinStartsAtTheBack()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);
        await BuildQueueSut([]).RunAsync(repository, _provider, null, Now.AddMinutes(2), TestContext.Current.CancellationToken);
        RefuseBehind();

        // Act
        await BuildSut(Pending(enteredAt: EnteredAt.AddHours(1)))
            .RunAsync(repository, _provider, null, Now.AddHours(1), TestContext.Current.CancellationToken);

        // Assert
        PendingMerge progress = LoadProgress();
        progress.ShouldSatisfyAllConditions(
            () => progress.QueuedAt.ShouldBe(Now.AddHours(1)),
            () => progress.CatchUpRounds.ShouldBe(0));
    }

    [Fact]
    public async Task WhenQueuedIssueGetsFeedback_ResolvesFeedbackAndLeavesTheQueue()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.Feedback = Result<ReviewFeedback>.Ok(new ReviewFeedback([new ReviewComment("Rename this.")], 0, Now));

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("feedback");
        LoadProgress().IsQueued.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSettingTurnedOffWhileQueued_ResolvesDisabledAndLeavesTheQueue()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        repository.SetMergeWhenChecksPass(false);

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        SingleResolved().Outcome.ShouldBe("disabled");
        LoadProgress().IsQueued.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheHeadWaitsForAnUpToDateIssue_RecordsThatIssue()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.Ancestry["feat/50-fresh"] = Result<bool>.Ok(true);

        // Act
        await BuildQueueSut([new MergeQueueIssueInfo(50, "feat/50-fresh", false, false)], Pending())
            .RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        LoadProgress().WaitingForIssueNumber.ShouldBe(50);
    }

    [Fact]
    public async Task WhenTheAwaitedIssueIsNoLongerUpToDate_ClearsTheAwaitedIssue()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _provider.Ancestry["feat/50-fresh"] = Result<bool>.Ok(true);
        await BuildQueueSut([new MergeQueueIssueInfo(50, "feat/50-fresh", false, false)], Pending())
            .RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);
        _provider.Ancestry["feat/50-fresh"] = Result<bool>.Ok(true);

        // Act: the awaited issue left the checks gate, and an earlier entry now heads the queue.
        PendingMerge earlier = PendingMerge.Start(repository.Id, 7, EnteredAt);
        MergeQueue.RefuseBehind(earlier, Now.AddDays(-1));
        _dbContext.Set<PendingMerge>().Add(earlier);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        await BuildQueueSut([], Pending(), Pending(7, "feat/7-earlier"))
            .RunAsync(repository, _provider, IssueNumber, Now.AddMinutes(2), TestContext.Current.CancellationToken);

        // Assert
        LoadProgress(IssueNumber).WaitingForIssueNumber.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAnEntryJoinsTheQueue_BroadcastsItsQueueChangeDirectly()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeQueueChanged changed = _processor.Events.OfType<MergeQueueChanged>().ShouldHaveSingleItem();
        changed.ShouldSatisfyAllConditions(
            () => changed.RepositoryId.ShouldBe(repository.Id),
            () => changed.IssueNumbers.ShouldBe([IssueNumber]));
        _dispatcher.DispatchedEvents.OfType<MergeQueueChanged>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenAnEntryAheadMerges_BroadcastsTheNewPositionOfTheWaitingEntryOnly()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        MergePendingIssueInfo first = Pending(41, "feat/41-first");
        MergePendingIssueInfo second = Pending(42, "feat/42-second");
        RefuseBehind(first.BranchName);
        RefuseBehind(second.BranchName);
        await BuildQueueSut([], first, second).RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        await BuildQueueSut([], first, second).RunAsync(repository, _provider, null, Now.AddMinutes(1), TestContext.Current.CancellationToken);
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        _processor.Events.Clear();

        // Act: the first entry is back from its catch-up and merges.
        await BuildQueueSut([], Pending(41, first.BranchName, EnteredAt.AddHours(1)), second)
            .RunAsync(repository, _provider, 41, Now.AddHours(1), TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.OfType<MergeResolved>().Last().Outcome.ShouldBe("merged");
        _processor.Events.OfType<MergeQueueChanged>().ShouldHaveSingleItem().IssueNumbers.ShouldBe([42]);
    }

    [Fact]
    public async Task WhenNothingInTheQueueChanges_BroadcastsNothing()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        MergePendingIssueInfo first = Pending(41, "feat/41-first");
        MergePendingIssueInfo second = Pending(42, "feat/42-second");
        RefuseBehind(first.BranchName);
        RefuseBehind(second.BranchName);
        await BuildQueueSut([], first, second).RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);
        _processor.Events.Clear();

        // Act: the later entry is nudged and keeps waiting behind the head.
        await BuildQueueSut([], first, second).RunAsync(repository, _provider, 42, Now.AddMinutes(1), TestContext.Current.CancellationToken);

        // Assert
        _processor.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheDirectBroadcastFails_StillPersistsTheQueue()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        RefuseBehind();
        _processor.Throws = true;

        // Act
        await BuildSut().RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        LoadProgress().AwaitsCatchUp.ShouldBeTrue();
    }

    // --- Marking the pull request ready for review before the merge ---

    private const string GitLabMrUrl = "https://gitlab.com/group/project/-/merge_requests/99";

    private void ReadDraft(bool isDraft) =>
        _provider.PullRequestState = Result<PullRequestState>.Ok(new PullRequestState.Open(CommitSha.From(VerifiedHead), isDraft));

    [Theory]
    [InlineData(PrUrl)]
    [InlineData(GitLabMrUrl)]
    public async Task WhenPullRequestIsAnUnmarkedDraft_MarksItReadyRecordsItThenMerges(string prUrl)
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReadDraft(isDraft: true);
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildSut(Pending(isMarkedReady: false, prUrl: prUrl))
            .RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldSatisfyAllConditions(
            () => _provider.MarkReadyCalls.ShouldBe([prUrl]),
            () => _provider.MergeCalls.ShouldHaveSingleItem().ShouldBe((prUrl, VerifiedHead)),
            () => _dispatcher.DispatchedEvents.Count.ShouldBe(2),
            () => _dispatcher.DispatchedEvents[0].ShouldBe(new PullRequestMarkedReady(repository.Id, IssueNumber, prUrl)),
            () => _dispatcher.DispatchedEvents[1].ShouldBeOfType<MergeResolved>().Outcome.ShouldBe("merged"));
    }

    [Fact]
    public async Task WhenMarkingTheDraftReadyFails_CountsATransientFailureWithoutMerging()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReadDraft(isDraft: true);
        _provider.MarkReadyResults.Enqueue(Result.Fail(RunPublishingErrors.Permanent("403 Forbidden")));

        // Act
        await BuildSut(Pending(isMarkedReady: false))
            .RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldBeEmpty();
        _provider.MergeCalls.ShouldBeEmpty();
        LoadProgress().TransientFailures.ShouldBe(1);
        DrainNudges().ShouldHaveSingleItem().Key.Pass.ShouldBe(NudgePass.Merge);
    }

    [Fact]
    public async Task WhenMarkingTheDraftReadyFailedOnTheLastAttempt_TheNextAttemptMarksItAndMerges()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReadDraft(isDraft: true);
        _provider.MarkReadyResults.Enqueue(Result.Fail(RunPublishingErrors.Transient("502 Bad Gateway")));
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));
        await BuildSut(Pending(isMarkedReady: false))
            .RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Act
        await BuildSut(Pending(isMarkedReady: false))
            .RunAsync(repository, _provider, null, Now.AddHours(1), TestContext.Current.CancellationToken);

        // Assert
        _provider.MarkReadyCalls.Count.ShouldBe(2);
        _dispatcher.DispatchedEvents.OfType<PullRequestMarkedReady>().ShouldHaveSingleItem();
        SingleResolved().Outcome.ShouldBe("merged");
    }

    [Fact]
    public async Task WhenMarkingTheDraftReadyKeepsFailing_ResolvesSkippedProviderError()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReadDraft(isDraft: true);
        DateTimeOffset now = Now;
        for (int attempt = 0; attempt < PendingMerge.MaxTransientFailures; attempt++)
        {
            _provider.MarkReadyResults.Enqueue(Result.Fail(RunPublishingErrors.Transient("502 Bad Gateway")));
        }

        // Act
        for (int attempt = 0; attempt < PendingMerge.MaxTransientFailures; attempt++)
        {
            await BuildSut(Pending(isMarkedReady: false))
                .RunAsync(repository, _provider, null, now, TestContext.Current.CancellationToken);
            now = now.AddHours(1);
        }

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.Outcome.ShouldBe("skipped");
        resolved.Reason.ShouldBe("provider_error");
        _provider.MergeCalls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PrUrl)]
    [InlineData(GitLabMrUrl)]
    public async Task WhenAMarkedPullRequestWasReDrafted_ResolvesSkippedDraftWithoutMerging(string prUrl)
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReadDraft(isDraft: true);

        // Act
        await BuildSut(Pending(isMarkedReady: true, prUrl: prUrl))
            .RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        MergeResolved resolved = SingleResolved();
        resolved.ShouldSatisfyAllConditions(
            () => resolved.Outcome.ShouldBe("skipped"),
            () => resolved.Reason.ShouldBe("draft"),
            () => _provider.MarkReadyCalls.ShouldBeEmpty(),
            () => _provider.MergeCalls.ShouldBeEmpty(),
            () => _dispatcher.DispatchedEvents.OfType<PullRequestMarkedReady>().ShouldBeEmpty());
    }

    [Theory]
    [InlineData(PrUrl)]
    [InlineData(GitLabMrUrl)]
    public async Task WhenAnUnmarkedPullRequestIsAlreadyReady_RecordsItWithoutCallingTheProviderThenMerges(string prUrl)
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReadDraft(isDraft: false);
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildSut(Pending(isMarkedReady: false, prUrl: prUrl))
            .RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _dispatcher.DispatchedEvents.ShouldSatisfyAllConditions(
            () => _provider.MarkReadyCalls.ShouldBeEmpty(),
            () => _dispatcher.DispatchedEvents.OfType<PullRequestMarkedReady>().ShouldHaveSingleItem()
                .ShouldBe(new PullRequestMarkedReady(repository.Id, IssueNumber, prUrl)),
            () => SingleResolved().Outcome.ShouldBe("merged"));
    }

    [Fact]
    public async Task WhenAMarkedPullRequestIsReady_MergesWithoutRecordingItAgain()
    {
        // Arrange
        MonitoredRepository repository = SeedRepository();
        ReadDraft(isDraft: false);
        _provider.Merges.Enqueue(Result<MergeRequestOutcome>.Ok(new MergeRequestOutcome.Merged()));

        // Act
        await BuildSut(Pending(isMarkedReady: true))
            .RunAsync(repository, _provider, null, Now, TestContext.Current.CancellationToken);

        // Assert
        _provider.MarkReadyCalls.ShouldBeEmpty();
        _dispatcher.DispatchedEvents.OfType<PullRequestMarkedReady>().ShouldBeEmpty();
        SingleResolved().Outcome.ShouldBe("merged");
    }

    private sealed class CapturingIntegrationEventProcessor : IIntegrationEventProcessor
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public bool Throws { get; set; }

        public Task ProcessAsync(Guid eventId, IIntegrationEvent @event, CancellationToken cancellationToken)
        {
            if (Throws)
            {
                throw new InvalidOperationException("Handler failed.");
            }

            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class StubIssueQueries(
        IReadOnlyList<MergePendingIssueInfo> mergePendingIssues,
        IReadOnlyList<MergeQueueIssueInfo> mergeQueueIssues) : IIssueQueries
    {
        public Task<IReadOnlyList<MergeQueueIssueInfo>> GetMergeQueueIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(mergeQueueIssues);

        public Task<IReadOnlyList<AwaitingInputIssueInfo>> GetAwaitingInputIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AwaitingInputIssueInfo>>([]);

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

        public Task<IReadOnlyList<ReviewStepIssueInfo>> GetReviewStepIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReviewStepIssueInfo>>([]);


        public Task<IReadOnlyList<QueuedRevisionIssueInfo>> GetQueuedRevisionIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>

            Task.FromResult<IReadOnlyList<QueuedRevisionIssueInfo>>([]);

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

        public Task<IReadOnlyList<MergePendingIssueInfo>> GetMergePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult(mergePendingIssues);

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RebasePendingIssueInfo>>([]);

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

        public Task<IReadOnlySet<int>> GetUnpublishedRunIssueNumbersAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

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
        public Queue<Result> MarkReadyResults { get; } = new();

        public List<string> MarkReadyCalls { get; } = [];

        public Task<Result> MarkPullRequestReadyAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
        {
            MarkReadyCalls.Add(pullRequestUrl);
            return Task.FromResult(MarkReadyResults.TryDequeue(out Result? result) ? result : Result.Ok());
        }

        public Queue<Result<MergeRequestOutcome>> Merges { get; } = new();

        public List<(string PullRequestUrl, string ExpectedHeadSha)> MergeCalls { get; } = [];

        public int IsClosedCalls { get; private set; }

        public bool IssueClosed { get; set; }

        public Result<PullRequestState> PullRequestState { get; set; } =
            Result<PullRequestState>.Ok(new PullRequestState.Open(CommitSha.From(VerifiedHead), IsDraft: false));

        public Exception? ThrowOnMerge { get; set; }

        public Result<ReviewFeedback> Feedback { get; set; } = Result<ReviewFeedback>.Ok(new ReviewFeedback([], 0, null));

        public List<(string PullRequestUrl, DateTimeOffset Since)> FeedbackReads { get; } = [];

        /// <summary>
        /// The ancestry answer per branch: whether the default-branch head is an ancestor of it. A branch without one
        /// fails permanently, as a provider without an ancestry read does.
        /// </summary>
        public Dictionary<string, Result<bool>> Ancestry { get; } = [];

        public List<(string AncestorRef, string BranchName)> AncestryReads { get; } = [];

        public Task<Result<bool>> IsAncestorAsync(
            RepositorySlug slug,
            string ancestorRef,
            string branchName,
            CancellationToken cancellationToken)
        {
            AncestryReads.Add((ancestorRef, branchName));
            return Task.FromResult(Ancestry.TryGetValue(branchName, out Result<bool>? answer)
                ? answer
                : Result<bool>.Fail(new Error(RunPublishingErrors.PermanentCode, "no ancestry read")));
        }

        public Task<Result<MergeRequestOutcome>> MergePullRequestAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            string expectedHeadSha,
            CancellationToken cancellationToken)
        {
            MergeCalls.Add((pullRequestUrl, expectedHeadSha));
            if (ThrowOnMerge is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(Merges.Dequeue());
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
            => throw new NotSupportedException();

        public Task<Result<PullRequestState>> GetPullRequestStateAsync(
            RepositorySlug slug,
            string pullRequestUrl,
            CancellationToken cancellationToken)
            => Task.FromResult(PullRequestState);

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
        {
            FeedbackReads.Add((pullRequestUrl, since));
            return Task.FromResult(Feedback);
        }

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
            CommitSha? publishedHead,
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
