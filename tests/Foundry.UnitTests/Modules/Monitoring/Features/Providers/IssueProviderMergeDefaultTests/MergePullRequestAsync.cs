using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Merge;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.IssueProviderMergeDefaultTests;

/// <summary>The default merge implementation for providers that have none (ADR 0105).</summary>
public sealed class MergePullRequestAsync
{
    private const string PrUrl = "https://github.com/owner/repo/pull/42";
    private const string HeadSha = "1111111111111111111111111111111111111111";

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task Default_AnswersUnsupported()
    {
        // Arrange
        IIssueProvider sut = new ProviderWithoutMerge();

        // Act
        Result<MergeRequestOutcome> result = await sut.MergePullRequestAsync(
            Slug, PrUrl, HeadSha, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<MergeRequestOutcome.Unsupported>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Default_WhenExpectedHeadIsBlank_Throws(string? expectedHeadSha)
    {
        // Arrange
        IIssueProvider sut = new ProviderWithoutMerge();

        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(() => sut.MergePullRequestAsync(
            Slug, PrUrl, expectedHeadSha!, TestContext.Current.CancellationToken));
    }

    // Keeps every default member; the required ones are never reached by the merge default.
    private sealed class ProviderWithoutMerge : IIssueProvider
    {
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
}
