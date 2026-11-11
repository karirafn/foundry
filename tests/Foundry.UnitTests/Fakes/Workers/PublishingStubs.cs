using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.UnitTests.Fakes.Workers;

/// <summary>Records the pull requests and summaries publishing asks for, and answers each with a scripted result.</summary>
internal sealed class RecordingRunPublishingCommands : IRunPublishingCommands
{
    private readonly List<(string Branch, string Title, string Body)> _opened = [];
    private readonly List<(string PullRequestUrl, string Body)> _summaries = [];

    public IReadOnlyList<(string Branch, string Title, string Body)> Opened => _opened;

    public IReadOnlyList<(string PullRequestUrl, string Body)> Summaries => _summaries;

    public Error? OpenError { get; init; }

    public Error? IdentityError { get; init; }

    public Task<Result<string>> OpenPullRequestAsync(
        MonitoredRepositoryId repositoryId,
        string branchName,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        if (OpenError is not null)
        {
            return Task.FromResult(Result<string>.Fail(OpenError));
        }

        _opened.Add((branchName, title, body));
        return Task.FromResult(Result<string>.Ok("https://github.com/owner/repo/pull/7"));
    }

    public Task<Result> PostRunSummaryAsync(
        MonitoredRepositoryId repositoryId,
        string pullRequestUrl,
        string body,
        CancellationToken cancellationToken)
    {
        _summaries.Add((pullRequestUrl, body));
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<CommitIdentity>> GetCommitIdentityAsync(
        MonitoredRepositoryId repositoryId,
        CancellationToken cancellationToken) =>
        Task.FromResult(IdentityError is null
            ? Result<CommitIdentity>.Ok(new CommitIdentity("Octo Cat", "1+octocat@users.noreply.github.com"))
            : Result<CommitIdentity>.Fail(IdentityError));

    public Task<Result<bool>> IsAncestorOfBranchAsync(
        MonitoredRepositoryId repositoryId,
        string ancestorRef,
        string branchName,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<bool>.Ok(false));
}

/// <summary>Answers the issue detail publishing reads for the pull request title and body; nothing else.</summary>
internal sealed class StubIssueQueries(int issueNumber = 42, string title = "Add retry", string? kind = "feature") : IIssueQueries
{
    public Task<Result<IssueDetail>> GetIssueDetailAsync(IssueId issueId, CancellationToken cancellationToken) =>
        Task.FromResult(Result<IssueDetail>.Ok(new IssueDetail(
            issueId.Value,
            issueNumber,
            title,
            "in_progress",
            "owner/repo",
            DateTimeOffset.UnixEpoch,
            $"https://github.com/owner/repo/issues/{issueNumber}",
            "author",
            [],
            StateDetails: null,
            new IssueKindDetails(kind is null ? IssueKindDetails.UnclassifiedSource : IssueKindDetails.LabelledSource, kind, null),
            Triage: null)));

    public Task<IReadOnlySet<int>> GetKnownIssueNumbersAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<int, IssueSnapshot>> GetIssueSnapshotsAsync(
        MonitoredRepositoryId repositoryId,
        IReadOnlySet<int> issueNumbers,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<DependencyEdge>> GetDependencyGraphAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ReviewIssueInfo>> GetReviewIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ChecksPendingIssueInfo>> GetChecksPendingIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<RebasePendingIssueInfo>> GetRebasePendingIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AwaitingInputIssueInfo>> GetAwaitingInputIssuesAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<int>> GetUntrackableIssueNumbersAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<int>> GetDispatchCandidateIssueNumbersAsync(MonitoredRepositoryId repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<IssueSummary>> GetIssueSummariesAsync(MonitoredRepositoryId? repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IssueSummary?> GetIssueSummaryAsync(IssueId issueId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<IssueSummary>> GetActiveIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        IReadOnlyCollection<string>? states,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PagedIssues> GetResolvedIssueSummariesAsync(
        MonitoredRepositoryId? repositoryId,
        IReadOnlyCollection<string> states,
        string? cursor,
        int limit,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IssueStateCounts> GetIssueStateCountsAsync(MonitoredRepositoryId? repositoryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
