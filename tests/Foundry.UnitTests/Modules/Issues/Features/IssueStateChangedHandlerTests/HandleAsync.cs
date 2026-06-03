using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.IssueStateChangedHandlerTests;

public sealed class HandleAsync
{
    private readonly StubIssueQueries _issueQueries;
    private readonly StubIssueBroadcaster _broadcaster;
    private readonly IssueStateChangedHandler _sut;

    public HandleAsync()
    {
        _issueQueries = new StubIssueQueries();
        _broadcaster = new StubIssueBroadcaster();
        _sut = new IssueStateChangedHandler(_issueQueries, _broadcaster);
    }

    [Fact]
    public async Task WhenIssueStateChanged_QueriesSummaryByIssueId()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        IssueSummary expectedSummary = new(
            Id: issueId.Value,
            IssueNumber: 1,
            Title: "Test Issue",
            State: "queued",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/1");
        _issueQueries.SetSummary(issueId, expectedSummary);

        IssueQueued @event = new(issueId, MonitoredRepositoryId.New());

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _issueQueries.QueriedIssueId.ShouldBe(issueId);
    }

    [Fact]
    public async Task WhenIssueStateChanged_BroadcastsIssueSummary()
    {
        // Arrange
        IssueId issueId = IssueId.New();
        IssueSummary expectedSummary = new(
            Id: issueId.Value,
            IssueNumber: 1,
            Title: "Test Issue",
            State: "queued",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/1");
        _issueQueries.SetSummary(issueId, expectedSummary);

        IssueQueued @event = new(issueId, MonitoredRepositoryId.New());

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _broadcaster.BroadcastedSummary.ShouldBe(expectedSummary);
    }

    [Fact]
    public async Task WhenIssueNotFound_DoesNotBroadcast()
    {
        // Arrange
        IssueQueued @event = new(IssueId.New(), MonitoredRepositoryId.New());

        // Act
        await _sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        _broadcaster.BroadcastedSummary.ShouldBeNull();
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

        public IssueId? QueriedIssueId { get; private set; }

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
            QueriedIssueId = issueId;
            _summaries.TryGetValue(issueId, out IssueSummary? summary);
            return Task.FromResult(summary);
        }

        public Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken)
            => Task.FromResult(Result<IssueDetail>.Fail(IssueErrors.NotFound(issueId)));

        public Task<IReadOnlyList<int>> GetDetectedAndIneligibleIssueNumbersAsync(
            MonitoredRepositoryId repositoryId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<int>>([]);
    }
}
