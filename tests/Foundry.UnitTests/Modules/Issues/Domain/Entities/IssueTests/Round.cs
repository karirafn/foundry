using System.Reflection;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

/// <summary>
/// An issue counts its revision rounds: a revision trigger (review findings, a person's feedback, failed checks, a
/// rebase conflict) starts a new round, and every other transition (a retry and a resume after answers included)
/// stays in the round it came from.
/// </summary>
public sealed class Round
{
    private const string Head = "4f2c9e1a7b3d5f6e8a0c2b4d6f8e0a1c3b5d7f9e";

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ReviewComment> Comments =
    [
        new ReviewComment("Please rename this.", "src/a.cs", 3),
    ];

    private static readonly IReadOnlyList<FailingCheck> Failing =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
    ];

    private static readonly RevisionTrigger.ReviewFindings Findings =
        new([], [new ReviewUnmetCriterion("AC1", "Retries on 5xx.", "No retry path.")], ReviewTestsAssessment.Adequate, "Covered.");

    private static IssueBuilder Builder() => new IssueBuilder()
        .WithVerifiedHeadSha(Head)
        .WithReviewComments(Comments)
        .WithFailingChecks(Failing);

    // Every RevisionQueuedIssue factory, and whether it starts a new round. A factory missing here fails
    // EveryRevisionQueuedFactory_IsDecided, so a new one cannot ship without deciding.
    private static readonly IReadOnlyDictionary<string, bool> StartsRoundByFactory = new Dictionary<string, bool>
    {
        ["FromReview"] = true,
        ["FromChecksPending"] = true,
        ["FromMergePending"] = true,
        ["FromReviewInProgress"] = true,
        ["FromChecksPendingFix"] = true,
        ["FromRebaseConflict"] = true,
        ["FromRevisionFailed"] = false,
        ["FromAwaitingInput"] = false,
        ["FromRevisionBlocked"] = false,
    };

    public static TheoryData<string> Factories => [.. StartsRoundByFactory.Keys];

    [Fact]
    public void WhenIssueIsDetected_IsOne()
    {
        // Act
        DetectedIssue detected = Builder().Detected();

        // Assert
        detected.Round.ShouldBe(1);
    }

    [Fact]
    public void WhenIssueReachesReviewWithoutRevision_IsOne()
    {
        // Act
        ReviewIssue review = Builder().Review();

        // Assert
        review.Round.ShouldBe(1);
    }

    [Fact]
    public void EveryRevisionQueuedFactory_IsDecided()
    {
        // Act
        string[] factories = typeof(RevisionQueuedIssue)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(RevisionQueuedIssue) && m.Name.StartsWith("From", StringComparison.Ordinal))
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        factories.ShouldBe(StartsRoundByFactory.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void WhenRevisionQueuedFactoryRuns_StartsARoundOnlyForARevisionTrigger(string factory)
    {
        // Arrange
        (Issue source, Func<RevisionQueuedIssue> transition) = Arrange(factory);
        int sourceRound = source.Round;

        // Act
        RevisionQueuedIssue revisionQueued = transition();

        // Assert
        revisionQueued.Round.ShouldBe(StartsRoundByFactory[factory] ? sourceRound + 1 : sourceRound);
    }

    public static TheoryData<string> CarryingPaths =>
    [
        "RevisionInProgress",
        "RebasePending",
        "ChecksPending",
        "Review",
        "ReviewQueued",
        "ReviewInProgress",
        "MergePending",
        "RevisionFailed",
        "PublishFailed",
        "PublishBlocked",
        "AwaitingInput",
        "BranchDivergedRetried",
        "ContinuableFailedRetried",
        "ContinuableFailedRetriedFresh",
        "Completed",
    ];

    [Theory]
    [MemberData(nameof(CarryingPaths))]
    public void WhenIssueMovesOnWithoutARevisionTrigger_KeepsTheRound(string path)
    {
        // Arrange
        RevisionQueuedIssue revisionQueued = Builder().RevisionQueued();

        // Act
        Issue issue = Follow(revisionQueued, path);

        // Assert
        issue.Round.ShouldBe(revisionQueued.Round);
    }

    [Fact]
    public void WhenIssueIsRevisedRepeatedly_CountsEveryRevisionTrigger()
    {
        // Arrange
        List<int> rounds = [];

        // Act
        InProgressIssue freshRun = Builder().InProgress();
        rounds.Add(freshRun.Round);

        RevisionQueuedIssue checksRed = ToChecksPending(freshRun).FixChecks(Failing, Now);
        rounds.Add(checksRed.Round);

        RevisionFailedIssue revisionFailed = checksRed
            .Claim(WorkerRunId.New(), Now)
            .MarkFailed("boom", FailureCategory.NonZeroExit, Now);
        rounds.Add(revisionFailed.Round);

        RevisionQueuedIssue retried = revisionFailed.Retry(Now);
        rounds.Add(retried.Round);

        RevisionFailedIssue usageLimited = retried
            .Claim(WorkerRunId.New(), Now)
            .MarkFailed(WorkerRunFailed.UsageLimitedReason, FailureCategory.UsageLimited, Now);
        rounds.Add(usageLimited.Round);

        RevisionQueuedIssue continued = usageLimited.Retry(Now);
        rounds.Add(continued.Round);

        RevisionQueuedIssue reviewFindings = ReviewFindingsFrom(continued);
        rounds.Add(reviewFindings.Round);

        // Assert
        rounds.ShouldBe([1, 2, 2, 2, 2, 2, 3]);
    }

    [Fact]
    public void WhenAPersonGivesFeedbackDuringRoundTwo_StartsRoundThree()
    {
        // Arrange
        ReviewIssue review = ToReview(Builder().ChecksPending().FixChecks(Failing, Now).Claim(WorkerRunId.New(), Now));

        // Act
        RevisionQueuedIssue revisionQueued = review.Revise(Comments, Now);

        // Assert
        revisionQueued.Round.ShouldBe(3);
    }

    private static (Issue Source, Func<RevisionQueuedIssue> Transition) Arrange(string factory)
    {
        switch (factory)
        {
            case "FromReview":
            {
                ReviewIssue review = Builder().Review();
                return (review, () => review.Revise(Comments, Now));
            }
            case "FromChecksPending":
            {
                ChecksPendingIssue checksPending = Builder().ChecksPending();
                return (checksPending, () => checksPending.Revise(Comments, Now));
            }
            case "FromMergePending":
            {
                MergePendingIssue mergePending = Builder().MergePending();
                return (mergePending, () => mergePending.Revise(Comments, Now));
            }
            case "FromReviewInProgress":
            {
                ReviewInProgressIssue reviewing = Builder().ReviewInProgress();
                return (reviewing, () => (RevisionQueuedIssue)reviewing.ResolveVerdict(
                    ReviewVerdictOutcome.ChangesRequested,
                    patchId: null,
                    Findings,
                    Now));
            }
            case "FromChecksPendingFix":
            {
                ChecksPendingIssue checksPending = Builder().ChecksPending();
                return (checksPending, () => checksPending.FixChecks(Failing, Now));
            }
            case "FromRebaseConflict":
            {
                RebasePendingIssue rebasePending = Builder().RebasePending();
                return (rebasePending, () => rebasePending.ResolveConflict(Now));
            }
            case "FromRevisionBlocked":
            {
                RevisionBlockedIssue blocked = Builder().RevisionBlocked([2]);
                return (blocked, () => blocked.Unblock(Now));
            }
            case "FromRevisionFailed":
            {
                RevisionFailedIssue failed = Builder().RevisionFailed();
                return (failed, () => failed.Retry(Now));
            }
            case "FromAwaitingInput":
            {
                AwaitingInputIssue awaiting = Builder().RevisionInProgress().MarkAwaitingInput(
                    IssueBuilder.DefaultQuestions,
                    Now,
                    Now);
                awaiting.RecordQuestionPosted(commentId: 1001, Now, commentUrl: null);
                return (awaiting, () => (RevisionQueuedIssue)awaiting.ProvideInput(
                    ClarificationInput.FromComments("Use the first."),
                    Now.AddMinutes(1),
                    Now.AddMinutes(1)));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(factory), factory, null);
        }
    }

    private static Issue Follow(RevisionQueuedIssue revisionQueued, string path)
    {
        RevisionInProgressIssue inProgress = revisionQueued.Claim(WorkerRunId.New(), Now);
        return path switch
        {
            "RevisionInProgress" => inProgress,
            "RebasePending" => inProgress.MarkRebasePending(Now, Now),
            "ChecksPending" => ToChecksPending(inProgress),
            "Review" => ToReview(inProgress),
            "ReviewQueued" => ToReviewQueued(inProgress),
            "ReviewInProgress" => ToReviewQueued(inProgress).Claim(WorkerRunId.New(), Now),
            "MergePending" => ToChecksPending(inProgress)
                .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: true, reviewPullRequests: false, Head, Now),
            "RevisionFailed" => inProgress.MarkFailed("boom", FailureCategory.NonZeroExit, Now),
            "PublishFailed" => inProgress.MarkPublishFailed("push refused", Now),
            "PublishBlocked" => inProgress.MarkPublishBlocked(Now),
            "AwaitingInput" => inProgress.MarkAwaitingInput(IssueBuilder.DefaultQuestions, Now, Now),
            "BranchDivergedRetried" => inProgress
                .MarkBranchDiverged("feat/1-test-run-1", BranchDivergenceReason.BranchMoved, Now)
                .Retry(Now),
            "ContinuableFailedRetried" => ToReview(inProgress).Fail("closed", FailureCategory.PrClosed, Now).Retry(Now),
            "ContinuableFailedRetriedFresh" =>
                ToReview(inProgress).Fail("closed", FailureCategory.PrClosed, Now).RetryFresh(Now),
            "Completed" => ToReview(inProgress).Complete(Now),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        };
    }

    private static RevisionQueuedIssue ReviewFindingsFrom(RevisionQueuedIssue revisionQueued) =>
        (RevisionQueuedIssue)ToReviewQueued(revisionQueued.Claim(WorkerRunId.New(), Now))
            .Claim(WorkerRunId.New(), Now)
            .ResolveVerdict(ReviewVerdictOutcome.ChangesRequested, patchId: null, Findings, Now);

    private static ChecksPendingIssue ToChecksPending(InProgressIssue inProgress) =>
        inProgress
            .MarkRebasePending("feat/1-test", "https://github.com/owner/repo/pull/1", Now, Now)
            .MarkChecksPending(new RebaseOutcome.UpToDate(), Now);

    private static ChecksPendingIssue ToChecksPending(RevisionInProgressIssue inProgress) =>
        inProgress.MarkRebasePending(Now, Now).MarkChecksPending(new RebaseOutcome.UpToDate(), Now);

    private static ReviewIssue ToReview(RevisionInProgressIssue inProgress) =>
        ToChecksPending(inProgress).ToReview(new ChecksOutcome.Passed(), Head, Now);

    private static ReviewQueuedIssue ToReviewQueued(RevisionInProgressIssue inProgress) =>
        (ReviewQueuedIssue)ToChecksPending(inProgress)
            .ResolveVerdict(new ChecksOutcome.Passed(), autoMerge: false, reviewPullRequests: true, Head, Now);
}
