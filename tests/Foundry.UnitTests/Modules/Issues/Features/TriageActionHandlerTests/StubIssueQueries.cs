using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.UnitTests.Modules.Issues.Features.TriageActionHandlerTests;

internal sealed class StubIssueQueries : IIssueQueries
{
    public Task<IReadOnlyList<AwaitingInputIssueInfo>> GetAwaitingInputIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AwaitingInputIssueInfo>>([]);

    public Task<Result<IssueDetail>> GetIssueDetailAsync(
        IssueId issueId,
        CancellationToken cancellationToken)
    {
        IssueDetail detail = new(
            Id: issueId.Value,
            IssueNumber: 1,
            Title: "stub",
            State: "queued",
            RepositorySlug: "owner/repo",
            DetectedAt: DateTimeOffset.UtcNow,
            Url: "https://github.com/owner/repo/issues/1",
            Author: "octocat",
            Labels: [],
            StateDetails: null,
            Kind: new IssueKindDetails(IssueKindDetails.UnclassifiedSource, null, null),
            Triage: null);
        return Task.FromResult(Result<IssueDetail>.Ok(detail));
    }

    public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

    public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
        MonitoredRepositoryId repositoryId,
        IReadOnlySet<int> issueNumbers,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<int, IssueSnapshot>>(new Dictionary<int, IssueSnapshot>());

    public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ReviewIssueInfo>>([]);

    public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RebasePendingIssueInfo>>([]);

    public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ChecksPendingIssueInfo>>([]);

    public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IssueSummary>>([]);

    public Task<IssueSummary?> GetIssueSummaryAsync(
        IssueId issueId,
        CancellationToken cancellationToken)
        => Task.FromResult<IssueSummary?>(null);

    public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<DependencyEdge>>([]);

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
