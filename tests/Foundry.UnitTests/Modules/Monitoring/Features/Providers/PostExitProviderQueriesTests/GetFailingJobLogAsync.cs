using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.PostExitProviderQueriesTests;

public sealed class GetFailingJobLogAsync : IAsyncDisposable
{
    private static readonly FailingCheck Check = new("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1/job/2");

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly JobLogProvider _provider = new();
    private readonly IFailingJobLogQueries _sut;

    public GetFailingJobLogAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();

        _sut = new PostExitProviderQueries(
            _dbContext,
            new SingleProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<MonitoredRepositoryId> SeedRepoAsync()
    {
        GitHubCredential credential = GitHubCredential.Create("my-org", "ghp_test_token", BaseUrl.Create("https://github.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("owner").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);

        MonitoredRepository repo = MonitoredRepository.Create(
            RepositorySlug.Create("owner/repo").ValueOrThrow(), "github.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return repo.Id;
    }

    private async Task<Result<CiJobLog>> ActAsync() =>
        await _sut.GetFailingJobLogAsync(await SeedRepoAsync(), Check, TestContext.Current.CancellationToken);

    [Fact]
    public async Task WhenTheCheckResolvesToAJob_ReturnsThatJobsLog()
    {
        // Arrange
        _provider.Lookup = Result<CiJobLookup>.Ok(new CiJobLookup.Found(2));
        _provider.Log = Result<string>.Ok("boom");

        // Act
        Result<CiJobLog> result = await ActAsync();

        // Assert
        result.ShouldBeOfType<Result<CiJobLog>.Success>().Value.ShouldBe(new CiJobLog.Available("boom"));
        _provider.RequestedJobIds.ShouldBe([2L]);
    }

    [Fact]
    public async Task WhenTheCheckIsNotACiJob_ReadsNoLog()
    {
        // Arrange
        _provider.Lookup = Result<CiJobLookup>.Ok(new CiJobLookup.NotACiJob());

        // Act
        Result<CiJobLog> result = await ActAsync();

        // Assert
        result.ShouldBeOfType<Result<CiJobLog>.Success>().Value.ShouldBeOfType<CiJobLog.NotACiJob>();
        _provider.RequestedJobIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenResolvingTheJobFails_Fails()
    {
        // Arrange
        _provider.Lookup = Result<CiJobLookup>.Fail(new Error("GitHub.ServerError", "boom"));

        // Act
        Result<CiJobLog> result = await ActAsync();

        // Assert
        result.ShouldBeOfType<Result<CiJobLog>.Failure>().Error.Code.ShouldBe("GitHub.ServerError");
    }

    [Fact]
    public async Task WhenReadingTheLogFails_Fails()
    {
        // Arrange
        _provider.Lookup = Result<CiJobLookup>.Ok(new CiJobLookup.Found(2));
        _provider.Log = Result<string>.Fail(JobLogErrors.Unavailable(2, 410));

        // Act
        Result<CiJobLog> result = await ActAsync();

        // Assert
        result.ShouldBeOfType<Result<CiJobLog>.Failure>().Error.Code.ShouldBe(JobLogErrors.UnavailableCode);
    }

    [Fact]
    public async Task WhenTheRepositoryIsUnknown_Fails()
    {
        // Act
        Result<CiJobLog> result = await _sut.GetFailingJobLogAsync(
            MonitoredRepositoryId.New(), Check, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<CiJobLog>.Failure>();
    }

    private sealed class SingleProviderFactory(IIssueProvider provider) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => provider;
    }

    // Only the job-log members are exercised; the rest are never reached by GetFailingJobLogAsync.
    private sealed class JobLogProvider : IIssueProvider
    {
        private readonly List<long> _requestedJobIds = [];

        public Result<CiJobLookup> Lookup { get; set; } = Result<CiJobLookup>.Ok(new CiJobLookup.NotACiJob());

        public Result<string> Log { get; set; } = Result<string>.Ok(string.Empty);

        public IReadOnlyList<long> RequestedJobIds => _requestedJobIds;

        public Task<Result<CiJobLookup>> ResolveCiJobAsync(RepositorySlug slug, FailingCheck check, CancellationToken cancellationToken) =>
            Task.FromResult(Lookup);

        public Task<Result<string>> GetJobLogAsync(RepositorySlug slug, long jobId, CancellationToken cancellationToken)
        {
            _requestedJobIds.Add(jobId);
            return Task.FromResult(Log);
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
