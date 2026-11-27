using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks.Delivery;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Delivery.WebhookDeliveryTests;

public sealed class LoadTrackingContextAsync
{
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();

    [Fact]
    public async Task CarriesTheAwaitingInputIssuesWithTheirPullRequestsAndBranches()
    {
        // Arrange — 40 waits on its own thread with no branch, 50 on its pull request of feat/50-revise.
        StubIssueQueries queries = new(
        [
            Waiting(40, pullRequestUrl: null, branchName: null),
            Waiting(50, pullRequestUrl: "https://github.com/owner/repo/pull/51", branchName: "feat/50-revise"),
        ]);

        // Act
        WebhookTrackingContext tracked = await WebhookDelivery.LoadTrackingContextAsync(
            queries,
            new StubTriageFailedIssueQueries(new HashSet<int>()),
            new StubSpecWatchedIssueQueries([]),
            RepoId,
            isIssueEvent: false,
            TestContext.Current.CancellationToken);

        // Assert
        tracked.AwaitingInput.ShouldBe(
        [
            new AwaitingInputThread(40, null, null),
            new AwaitingInputThread(50, "https://github.com/owner/repo/pull/51", "feat/50-revise"),
        ]);
    }

    [Fact]
    public async Task ForAnIssueEvent_CarriesTheTriageFailedAndSpecWatchedIssueNumbers()
    {
        // Arrange
        StubIssueQueries queries = new([]);

        // Act
        WebhookTrackingContext tracked = await WebhookDelivery.LoadTrackingContextAsync(
            queries,
            new StubTriageFailedIssueQueries(new HashSet<int> { 80 }),
            new StubSpecWatchedIssueQueries([new SpecWatchedIssue(90, null), new SpecWatchedIssue(91, null)]),
            RepoId,
            isIssueEvent: true,
            TestContext.Current.CancellationToken);

        // Assert
        tracked.ShouldSatisfyAllConditions(
            () => tracked.TriageFailed.ShouldBe(new HashSet<int> { 80 }),
            () => tracked.SpecWatched.ShouldBe(new HashSet<int> { 90, 91 }, ignoreOrder: true));
    }

    [Fact]
    public async Task ForAnyOtherEvent_LoadsNoTriageFailedOrSpecWatchedIssueNumbers()
    {
        // Arrange
        StubIssueQueries queries = new([]);

        // Act
        WebhookTrackingContext tracked = await WebhookDelivery.LoadTrackingContextAsync(
            queries,
            new StubTriageFailedIssueQueries(new HashSet<int> { 80 }),
            new StubSpecWatchedIssueQueries([new SpecWatchedIssue(90, null)]),
            RepoId,
            isIssueEvent: false,
            TestContext.Current.CancellationToken);

        // Assert
        tracked.ShouldSatisfyAllConditions(
            () => tracked.TriageFailedIssueNumbers.ShouldBeNull(),
            () => tracked.SpecWatchedIssueNumbers.ShouldBeNull());
    }

    private sealed class StubTriageFailedIssueQueries(IReadOnlySet<int> failed) : ITriageFailedIssueQueries
    {
        public Task<IReadOnlySet<int>> GetTriageFailedIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(failed);
    }

    private sealed class StubSpecWatchedIssueQueries(IReadOnlyList<SpecWatchedIssue> watched) : ISpecWatchedIssueQueries
    {
        public Task<IReadOnlyList<SpecWatchedIssue>> GetSpecWatchedIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(watched);
    }

    private static AwaitingInputIssueInfo Waiting(int issueNumber, string? pullRequestUrl, string? branchName) =>
        new(
            IssueId.New(),
            issueNumber,
            $"https://github.com/owner/repo/issues/{issueNumber}",
            pullRequestUrl,
            branchName,
            [new InputQuestion.Legacy("Which?", "Context.", null)],
            [],
            new AwaitingInputPostState.Posted(DateTimeOffset.UnixEpoch, CommentUrl: null, ReminderDueAt: null));

    private sealed class StubIssueQueries(IReadOnlyList<AwaitingInputIssueInfo> awaitingInput) : IIssueQueries
    {
        public Task<IReadOnlyList<AwaitingInputIssueInfo>> GetAwaitingInputIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(awaitingInput);

        public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlyList<ReviewStepIssueInfo>> GetReviewStepIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReviewStepIssueInfo>>([]);


        public Task<IReadOnlyList<QueuedRevisionIssueInfo>> GetQueuedRevisionIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>

            Task.FromResult<IReadOnlyList<QueuedRevisionIssueInfo>>([]);

        public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

        public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

        public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
            MonitoredRepositoryId repositoryId,
            IReadOnlySet<int> issueNumbers,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

        public Task<IReadOnlyList<MergePendingIssueInfo>> GetMergePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MergePendingIssueInfo>>([]);

        public Task<IReadOnlyList<MergeQueueIssueInfo>> GetMergeQueueIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MergeQueueIssueInfo>>([]);

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlySet<int>> GetUnpublishedRunIssueNumbersAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IssueSummary?> GetIssueSummaryAsync(IssueId issueId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string>? states,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PagedIssues> GetResolvedIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            IReadOnlyCollection<string> states,
            string? cursor,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IssueStateCounts> GetIssueStateCountsAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
