using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;
using Foundry.Testing;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Fakes.Monitoring;

/// <summary>Records blocker-link and comment calls, with a configurable result per blocker and per comment.</summary>
internal sealed class RecordingBlockerLinkProvider : IIssueProvider
{
    private readonly Dictionary<int, Result> _linkResults = [];

    public List<string> Calls { get; } = [];

    public Exception? ThrowOnCall { get; set; }

    public Action? OnCall { get; set; }

    public Result DefaultLinkResult { get; set; } = Result.Ok();

    public Result<ProviderCommentId> Comment { get; set; } =
        Result<ProviderCommentId>.Ok(ProviderCommentId.Create(555).ValueOrThrow());

    public void SetLinkResult(int blockerNumber, Result result) => _linkResults[blockerNumber] = result;

    public Task<Result> AddBlockerAsync(
        RepositorySlug slug, int issueNumber, int blockerNumber, CancellationToken cancellationToken)
    {
        Record($"link {slug} #{issueNumber} blocked by #{blockerNumber}");
        return Task.FromResult(_linkResults.GetValueOrDefault(blockerNumber, DefaultLinkResult));
    }

    public Task<Result<ProviderCommentId>> PostCommentAsync(
        RepositorySlug slug, CommentThread thread, string body, CancellationToken cancellationToken)
    {
        Record($"comment {slug} {thread} {body}");
        return Task.FromResult(Comment);
    }

    private void Record(string call)
    {
        Calls.Add(call);
        OnCall?.Invoke();
        if (ThrowOnCall is { } exception)
        {
            throw exception;
        }
    }

    public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<bool>> IsIssueClosedAsync(RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(RepositorySlug slug, string pullRequestUrl, DateTimeOffset since, PostedCommentIds postedComments, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<string>> CreatePullRequestAsync(RepositorySlug slug, string sourceBranch, string title, string body, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<CommitIdentity>> GetAuthenticatedIdentityAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<bool>> IsAncestorAsync(RepositorySlug slug, string ancestorRef, string branchName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<BranchProtection>> GetBranchProtectionAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<bool>> CreateBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<bool>> DeleteBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ProviderChecks>> GetChecksAsync(RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
