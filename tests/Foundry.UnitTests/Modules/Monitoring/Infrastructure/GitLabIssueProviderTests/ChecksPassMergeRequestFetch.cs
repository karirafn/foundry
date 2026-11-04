using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

/// <summary>
/// The checks pass reads a merge request's status and then its checks verdict. GitLab returns both from
/// one <c>GET projects/:id/merge_requests/:iid</c>, so the pair costs a single fetch.
/// </summary>
public sealed class ChecksPassMergeRequestFetch
{
    private const string MergeRequestUrl = "https://gitlab.com/group/project/-/merge_requests/7";
    private const string OpenMergeRequestWithGreenPipeline = """{"iid":7,"state":"opened","head_pipeline":{"status":"success"}}""";

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenStatusThenChecksForSameMergeRequest_FetchesMergeRequestOnce()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, OpenMergeRequestWithGreenPipeline);
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<PullRequestStatus> status = await sut.GetPullRequestStatusAsync(Slug, MergeRequestUrl, CancellationToken.None);
        Result<ProviderChecks> checks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        status.ShouldBeOfType<Result<PullRequestStatus>.Success>().Value.IsClosed.ShouldBeFalse();
        checks.ShouldBeOfType<Result<ProviderChecks>.Success>().Value.ShouldBeOfType<ProviderChecks.Green>();
        handler.AllRequests.Count.ShouldBe(1);
        handler.AllRequests[0].RequestUri!.AbsolutePath.ShouldEndWith("/merge_requests/7");
    }

    [Fact]
    public async Task WhenChecksReadTwice_SecondReadFetchesAgain()
    {
        // Arrange — the verdict read with the status is consumed once, never served stale.
        FakeHandler handler = new(HttpStatusCode.OK, OpenMergeRequestWithGreenPipeline);
        GitLabIssueProvider sut = CreateProvider(handler);
        await sut.GetPullRequestStatusAsync(Slug, MergeRequestUrl, CancellationToken.None);
        await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Act
        Result<ProviderChecks> checks = await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        checks.ShouldBeOfType<Result<ProviderChecks>.Success>().Value.ShouldBeOfType<ProviderChecks.Green>();
        handler.AllRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenChecksReadWithoutStatus_FetchesMergeRequest()
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
    public async Task WhenStatusReadFails_PropagatesFailureAndChecksFetchesAgain()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, "{}");
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<PullRequestStatus> status = await sut.GetPullRequestStatusAsync(Slug, MergeRequestUrl, CancellationToken.None);
        await sut.GetChecksAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        status.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        handler.AllRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenMergeRequestMerged_ReportsClosedAndMerged()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, """{"iid":7,"state":"merged","head_pipeline":null}""");
        GitLabIssueProvider sut = CreateProvider(handler);

        // Act
        Result<PullRequestStatus> status = await sut.GetPullRequestStatusAsync(Slug, MergeRequestUrl, CancellationToken.None);

        // Assert
        PullRequestStatus value = status.ShouldBeOfType<Result<PullRequestStatus>.Success>().Value;
        value.IsClosed.ShouldBeTrue();
        value.IsMerged.ShouldBeTrue();
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
