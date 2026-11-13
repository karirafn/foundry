using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.WebhookDeliveryTests;

public sealed class LoadTrackingContextAsync
{
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();

    [Fact]
    public async Task CarriesTheAwaitingInputIssuesAndTheirPullRequests()
    {
        // Arrange — 40 waits on its own thread, 50 on its pull request.
        StubIssueQueries queries = new(
        [
            Waiting(40, pullRequestUrl: null),
            Waiting(50, pullRequestUrl: "https://github.com/owner/repo/pull/51"),
        ]);

        // Act
        WebhookTrackingContext tracked = await WebhookDelivery.LoadTrackingContextAsync(
            queries,
            RepoId,
            includeBlockers: false,
            TestContext.Current.CancellationToken);

        // Assert
        tracked.AwaitingInput.ShouldBe(
        [
            new AwaitingInputThread(40, null),
            new AwaitingInputThread(50, "https://github.com/owner/repo/pull/51"),
        ]);
    }

    private static AwaitingInputIssueInfo Waiting(int issueNumber, string? pullRequestUrl) =>
        new(
            IssueId.New(),
            issueNumber,
            $"https://github.com/owner/repo/issues/{issueNumber}",
            pullRequestUrl,
            [new InputQuestion("Which?", "Context.", null)],
            new AwaitingInputPostState.Posted(DateTimeOffset.UnixEpoch));

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
            throw new NotImplementedException();

        public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

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
