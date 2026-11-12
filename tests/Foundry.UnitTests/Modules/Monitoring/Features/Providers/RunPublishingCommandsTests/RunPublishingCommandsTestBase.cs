using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Publishing;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.RunPublishingCommandsTests;

public abstract class RunPublishingCommandsTestBase : IAsyncDisposable
{
    protected static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;

    protected RunPublishingCommandsTestBase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        DbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        DbContext.Database.EnsureCreated();

        Sut = new RunPublishingCommands(
            DbContext,
            new SingleIssueProviderFactory(Provider),
            new CredentialResolver(DbContext, NullLogger<CredentialResolver>.Instance),
            new PostedCommentRegistry(DbContext),
            new FakeTimeProvider(Now));
    }

    internal FoundryDbContext DbContext { get; }

    internal PublishingProvider Provider { get; } = new();

    internal IRunPublishingCommands Sut { get; }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected async Task<MonitoredRepositoryId> SeedRepoAsync()
    {
        GitHubCredential credential = GitHubCredential.Create(
            "my-org", "ghp_test_token", BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        DbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        DbContext.Set<MonitoredRepository>().Add(repo);
        await DbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    // Only the publishing operations are exercised; the rest are never reached by RunPublishingCommands.
    internal sealed class PublishingProvider : IIssueProvider
    {
        public List<string> Calls { get; } = [];

        public Exception? ThrowOnCall { get; set; }

        public Result<string> PullRequest { get; set; } = Result<string>.Ok("https://github.com/owner/repo/pull/1");

        public Result<ProviderCommentId> Comment { get; set; } =
            Result<ProviderCommentId>.Ok(ProviderCommentId.Create(555).ValueOrThrow());

        public Result<CommitIdentity> Identity { get; set; } =
            Result<CommitIdentity>.Ok(new CommitIdentity("Octo", "1+octo@users.noreply.github.com"));

        public Result<bool> Ancestry { get; set; } = Result<bool>.Ok(true);

        public Task<Result<string>> CreatePullRequestAsync(
            RepositorySlug slug, string sourceBranch, string title, string body, CancellationToken cancellationToken)
        {
            Record($"create-pr {slug} {sourceBranch} {title} {body}");
            return Task.FromResult(PullRequest);
        }

        public Task<Result<ProviderCommentId>> PostCommentAsync(
            RepositorySlug slug, CommentThread thread, string body, CancellationToken cancellationToken)
        {
            Record($"comment {slug} {thread} {body}");
            return Task.FromResult(Comment);
        }

        public Task<Result<CommitIdentity>> GetAuthenticatedIdentityAsync(CancellationToken cancellationToken)
        {
            Record("identity");
            return Task.FromResult(Identity);
        }

        public Task<Result<bool>> IsAncestorAsync(
            RepositorySlug slug, string ancestorRef, string branchName, CancellationToken cancellationToken)
        {
            Record($"ancestor {slug} {ancestorRef} {branchName}");
            return Task.FromResult(Ancestry);
        }

        private void Record(string call)
        {
            Calls.Add(call);
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
