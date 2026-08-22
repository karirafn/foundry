using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.StateChanges.IssueStateChangedHandlerTests;

public sealed class AdapterHandleAsync
{
    [Fact]
    public async Task WhenIssueStateChangedEventReceived_AdapterDelegatesToHandler()
    {
        // Arrange
        StubIssueBroadcaster broadcaster = new();
        StubIssueQueries issueQueries = new();
        IssueStateChangedHandler handler = new(issueQueries, broadcaster);
        IssueStateChangedAdapter<IssueQueued> adapter = new(handler);

        IssueId issueId = IssueId.New();
        IssueSummary expectedSummary = new(
            Id: issueId.Value,
            IssueNumber: 7,
            Title: "Test",
            State: "queued",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/7",
            FailureClassification: null,
            RepositoryEligibilityStatus: null,
            RunStats: null);
        issueQueries.SetSummary(issueId, expectedSummary);

        IssueQueued @event = new(issueId, MonitoredRepositoryId.New());

        // Act
        await adapter.HandleAsync(@event, CancellationToken.None);

        // Assert
        broadcaster.BroadcastedSummary.ShouldBe(expectedSummary);
    }

    [Fact]
    public async Task WhenAdapterHandlesViaNonGenericInterface_DelegatesToHandler()
    {
        // Arrange
        StubIssueBroadcaster broadcaster = new();
        StubIssueQueries issueQueries = new();
        IssueStateChangedHandler handler = new(issueQueries, broadcaster);
        IDomainEventHandler<IssueQueued> adapter = new IssueStateChangedAdapter<IssueQueued>(handler);

        IssueId issueId = IssueId.New();
        IssueSummary expectedSummary = new(
            Id: issueId.Value,
            IssueNumber: 3,
            Title: "Issue",
            State: "queued",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/3",
            FailureClassification: null,
            RepositoryEligibilityStatus: null,
            RunStats: null);
        issueQueries.SetSummary(issueId, expectedSummary);

        IssueQueued @event = new(issueId, MonitoredRepositoryId.New());

        // Act
        await ((IDomainEventHandler)adapter).HandleAsync(@event, CancellationToken.None);

        // Assert
        broadcaster.BroadcastedSummary.ShouldBe(expectedSummary);
    }

    private sealed class StubIssueBroadcaster : IIssueBroadcaster
    {
        public IssueSummary? BroadcastedSummary { get; private set; }

        public Task BroadcastAsync(IssueSummary summary, CancellationToken cancellationToken)
        {
            BroadcastedSummary = summary;
            return Task.CompletedTask;
        }
    }

    private sealed class StubIssueQueries : IIssueQueries
    {
        private readonly Dictionary<IssueId, IssueSummary> _summaries = [];

        public void SetSummary(IssueId issueId, IssueSummary summary) => _summaries[issueId] = summary;

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

        public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
            MonitoredRepositoryId? repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

        public Task<IssueSummary?> GetIssueSummaryAsync(IssueId issueId, CancellationToken cancellationToken)
        {
            _summaries.TryGetValue(issueId, out IssueSummary? summary);
            return Task.FromResult(summary);
        }

        public Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));

        public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

        public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

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
}
