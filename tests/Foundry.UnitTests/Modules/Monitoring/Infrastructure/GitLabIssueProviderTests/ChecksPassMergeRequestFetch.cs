using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitLabChecksTests;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

/// <summary>
/// The checks pass reads a merge request's state and then its checks verdict. GitLab returns both from
/// one <c>GET projects/:id/merge_requests/:iid</c>, so the pair costs a single fetch.
/// </summary>
public sealed class ChecksPassMergeRequestFetch
{
    private const string MergeRequestUrl = "https://gitlab.com/group/project/-/merge_requests/7";
    private const string OpenMergeRequestWithGreenPipeline = """{"iid":7,"state":"opened","sha":"8888888888888888888888888888888888888888","head_pipeline":{"status":"success"}}""";

    private const string OpenMergeRequestWithFailedPipeline = """{"iid":7,"state":"opened","sha":"8888888888888888888888888888888888888888","head_pipeline":{"id":6,"status":"failed"}}""";

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenChecksReadTwice_SecondReadFetchesAgain()
    {
        // Arrange — the verdict read with the state is consumed once, never served stale.
        FakeHandler handler = new(HttpStatusCode.OK, OpenMergeRequestWithGreenPipeline);
        GitLabIssueProvider sut = CreateProvider(handler);
        await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);
        await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Act
        Result<ProviderChecks> checks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        checks.ShouldBeOfType<Result<ProviderChecks>.Success>().Value.ShouldBeOfType<ProviderChecks.Green>();
        handler.AllRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenChecksReadWithoutState_FetchesMergeRequest()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{"iid":7,"state":"opened","head_pipeline":{"status":"failed"}}""");
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<ProviderChecks> checks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        checks.ShouldBeOfType<Result<ProviderChecks>.Success>().Value.ShouldBeOfType<ProviderChecks.Red>();
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenStateThenChecksForFailedPipeline_NamesFailingJobAfterTwoRequests()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, OpenMergeRequestWithFailedPipeline)
            .WithRoute("/pipelines/6/jobs", HttpStatusCode.OK, GitLabJobsByPipelineExample.OneFailedJob());
        GitLabIssueProvider sut = CreateProvider(handler);
        await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Act
        Result<ProviderChecks> checks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        ProviderChecks.Red red = checks
            .ShouldBeOfType<Result<ProviderChecks>.Success>().Value
            .ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.Select(check => check.Name).ShouldBe(["rspec:other"]);
        handler.AllRequests.Count.ShouldBe(2);
        handler.AllRequests[1].RequestUri!.AbsolutePath.ShouldEndWith("/pipelines/6/jobs");
    }

    [Fact]
    public async Task WhenStateReadAlone_MakesOneRequestAndNoJobsRead()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, OpenMergeRequestWithFailedPipeline)
            .WithRoute("/pipelines/6/jobs", HttpStatusCode.OK, GitLabJobsByPipelineExample.OneFailedJob());
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenJobsReadFailsOnCachedPath_StateSucceedsChecksFailAndNextChecksRereadsMergeRequest()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, OpenMergeRequestWithFailedPipeline)
            .WithRoute("/pipelines/6/jobs", HttpStatusCode.ServiceUnavailable, "{}");
        GitLabIssueProvider sut = CreateProvider(handler);
        Result<PullRequestState> state = await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);
        Result<ProviderChecks> firstChecks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Act
        await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        state.ShouldBeOfType<Result<PullRequestState>.Success>();
        firstChecks.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        handler.AllRequests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/merge_requests/7", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task WhenMergeRequestMerged_ReportsMerged()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{"iid":7,"state":"merged","head_pipeline":null}""");
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<PullRequestState> state = await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        state.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Merged>();
    }

    [Fact]
    public async Task WhenStateThenChecksForSameMergeRequest_FetchesMergeRequestOnce()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{"iid":7,"state":"opened","sha":"8888888888888888888888888888888888888888","head_pipeline":{"status":"success"}}""");
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<PullRequestState> state = await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);
        Result<ProviderChecks> checks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        state.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Open>();
        checks.ShouldBeOfType<Result<ProviderChecks>.Success>().Value.ShouldBeOfType<ProviderChecks.Green>();
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenStateReadsAnOpenMergeRequestWithoutHead_FailsAndChecksStillUseTheSameFetch()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{"iid":7,"state":"opened","head_pipeline":{"status":"success"}}""");
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<PullRequestState> state = await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);
        Result<ProviderChecks> checks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        state.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code.ShouldBe(PullRequestStateErrors.MissingHeadCode);
        checks.ShouldBeOfType<Result<ProviderChecks>.Success>().Value.ShouldBeOfType<ProviderChecks.Green>();
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenStateReadFails_PropagatesFailureAndChecksFetchesAgain()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, "{}");
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<PullRequestState> state = await sut.GetPullRequestStateAsync(Slug, MergeRequestUrl, CancellationToken.None);
        await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        state.ShouldBeOfType<Result<PullRequestState>.Failure>();
        handler.AllRequests.Count.ShouldBe(2);
    }

    private static GitLabIssueProvider CreateProvider(FakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitLabIssueProvider(
            gitLabHttpClient,
            new ActionableFeedbackPolicy(TimeProvider.System),
            "glpat_token",
            new Uri("https://gitlab.com/api/v4"));
    }
}
