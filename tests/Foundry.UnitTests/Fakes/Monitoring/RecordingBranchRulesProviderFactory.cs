using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;

namespace Foundry.UnitTests.Fakes.Monitoring;

/// <summary>
/// Records every branch-rules write and webhook upsert (slug, token and spec) and answers each with a
/// per-slug scripted result. Unscripted slugs succeed (webhooks: <see cref="WebhookUpsertKind.Created"/>).
/// Every other provider call throws.
/// </summary>
internal sealed class RecordingBranchRulesProviderFactory : IIssueProviderFactory
{
    private readonly Dictionary<string, Result> _results = [];
    private readonly Dictionary<string, Exception> _exceptions = [];

    public List<string> TokensUsed { get; } = [];

    public List<string> AppliedSlugs { get; } = [];

    public List<(string Slug, WebhookHookSpec Spec)> WebhookUpserts { get; } = [];

    private readonly Dictionary<string, Result<WebhookUpsertKind>> _webhookResults = [];

    public RecordingBranchRulesProviderFactory WithWebhookResult(string slug, Result<WebhookUpsertKind> result)
    {
        _webhookResults[slug] = result;
        return this;
    }

    public RecordingBranchRulesProviderFactory WithResult(string slug, Result result)
    {
        _results[slug] = result;
        return this;
    }

    public RecordingBranchRulesProviderFactory WithException(string slug, Exception exception)
    {
        _exceptions[slug] = exception;
        return this;
    }

    public IIssueProvider CreateProvider(Credential credential, string token)
    {
        TokensUsed.Add(token);
        return new Provider(this);
    }

    private sealed class Provider(RecordingBranchRulesProviderFactory owner) : IIssueProvider
    {
        public Task<Result> ApplyBranchRulesAsync(RepositorySlug slug, CancellationToken cancellationToken)
        {
            owner.AppliedSlugs.Add(slug.FullPath);
            if (owner._exceptions.TryGetValue(slug.FullPath, out Exception? exception))
            {
                throw exception;
            }

            return Task.FromResult(owner._results.TryGetValue(slug.FullPath, out Result? result) ? result : Result.Ok());
        }

        public Task<Result<WebhookUpsertKind>> UpsertWebhookAsync(
            RepositorySlug slug,
            WebhookHookSpec spec,
            CancellationToken cancellationToken)
        {
            owner.WebhookUpserts.Add((slug.FullPath, spec));
            if (owner._exceptions.TryGetValue(slug.FullPath, out Exception? exception))
            {
                throw exception;
            }

            return Task.FromResult(owner._webhookResults.TryGetValue(slug.FullPath, out Result<WebhookUpsertKind>? result)
                ? result
                : Result<WebhookUpsertKind>.Ok(WebhookUpsertKind.Created));
        }

        public Task<Result<IssueListing>> GetIssuesAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<int>>> GetDependenciesAsync(
            RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> IsIssueClosedAsync(
            RepositorySlug slug, int issueNumber, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<PullRequestStatus>> GetPullRequestStatusAsync(
            RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ReviewFeedback>> GetReviewFeedbackAsync(
            RepositorySlug slug, string pullRequestUrl, DateTimeOffset since, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchProtection>> GetBranchProtectionAsync(
            RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CreateBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> DeleteBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<MergeRequestByBranch>> GetMergeRequestByBranchAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<BranchCommitSummary>> GetBranchCommitSummaryAsync(
            RepositorySlug slug, string branchName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<bool>> CanPushAsync(RepositorySlug slug, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<Checks>> GetChecksAsync(
            RepositorySlug slug, string pullRequestUrl, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
