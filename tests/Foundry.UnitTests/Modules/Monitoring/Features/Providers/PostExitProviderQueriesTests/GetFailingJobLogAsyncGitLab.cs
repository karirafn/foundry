using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Contracts.Queries;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.CredentialResolution;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Checks;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitLabChecksTests;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;
using Foundry.WebApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.PostExitProviderQueriesTests;

public sealed class GetFailingJobLogAsyncGitLab : IAsyncDisposable
{
    private const string TraceBody = "boom";
    private const string MergeRequestUrl = "https://gitlab.com/foo/bar/-/merge_requests/7";
    private const string ExpectedTraceRequest = "/api/v4/projects/foo%2Fbar/jobs/6/trace";

    private readonly SqliteConnection _connection;
    private readonly FoundryDbContext _dbContext;
    private readonly FakeHandler _handler;
    private readonly GitLabIssueProvider _provider;
    private readonly IFailingJobLogQueries _sut;

    public GetFailingJobLogAsyncGitLab()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = new FoundryDbContext(new DbContextOptionsBuilder<FoundryDbContext>().UseSqlite(_connection).Options);
        _dbContext.Database.EnsureCreated();

        _handler = new FakeHandler(
            HttpStatusCode.OK,
            $$"""{ "iid": 7, "state": "opened", "head_pipeline": { "id": {{GitLabJobsByPipelineExample.PipelineId}}, "status": "failed" } }""")
            .WithRoute($"/pipelines/{GitLabJobsByPipelineExample.PipelineId}/jobs", HttpStatusCode.OK, GitLabJobsByPipelineExample.OneFailedJob())
            .WithRoute($"/jobs/{GitLabJobsByPipelineExample.PipelineId}/trace", HttpStatusCode.OK, TraceBody);

        GitLabHttpClient httpClient = new(
            new HttpClient(_handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        _provider = new GitLabIssueProvider(
            httpClient,
            new ActionableFeedbackPolicy(TimeProvider.System),
            "glpat_token",
            new Uri("https://gitlab.com/api/v4"));

        _sut = new PostExitProviderQueries(
            _dbContext,
            new SingleProviderFactory(_provider),
            new CredentialResolver(_dbContext, NullLogger<CredentialResolver>.Instance));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _handler.Dispose();
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheCheckCameFromTheFailedJobsRead_ReturnsThatJobsTrace()
    {
        // Arrange
        GitLabCredential credential = GitLabCredential.Create("my-gitlab", "glpat_token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());
        credential.SetNamespaces([Namespace.Create("foo").ValueOrThrow()]);
        _dbContext.Set<Credential>().Add(credential);
        RepositorySlug slug = RepositorySlug.Create("foo/bar").ValueOrThrow();
        MonitoredRepository repo = MonitoredRepository.Create(slug, "gitlab.com", null).ValueOrThrow();
        _dbContext.Set<MonitoredRepository>().Add(repo);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        ProviderChecks checks = (await _provider.GetChecksAsync(slug, MergeRequestUrl, TestContext.Current.CancellationToken))
            .ShouldBeOfType<Result<ProviderChecks>.Success>().Value;
        FailingCheck check = checks.ShouldBeOfType<ProviderChecks.Red>().FailingChecks.ShouldHaveSingleItem();

        // Act
        Result<CiJobLog> result = await _sut.GetFailingJobLogAsync(repo.Id, check, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<CiJobLog>.Success>().Value.ShouldBe(new CiJobLog.Available(TraceBody));
        _handler.AllRequests.ShouldContain(request => request.RequestUri!.AbsoluteUri.EndsWith(ExpectedTraceRequest, StringComparison.Ordinal));
    }

    private sealed class SingleProviderFactory(IIssueProvider provider) : IIssueProviderFactory
    {
        public IIssueProvider CreateProvider(Credential credential, string token) => provider;
    }
}
