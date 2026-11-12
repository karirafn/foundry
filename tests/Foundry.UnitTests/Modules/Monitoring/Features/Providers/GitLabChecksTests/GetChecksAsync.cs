using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitLabChecksTests;

public sealed class GetChecksAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidMrUrl = "https://gitlab.com/group/project/-/merge_requests/42";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static FakeHandler FailedPipelineHandler(string jobsJson)
    {
        string mergeRequestJson =
            $$"""{ "iid": 42, "state": "opened", "head_pipeline": { "id": {{GitLabJobsByPipelineExample.PipelineId}}, "status": "failed" } }""";

        return new FakeHandler(HttpStatusCode.OK, mergeRequestJson)
            .WithRoute($"/pipelines/{GitLabJobsByPipelineExample.PipelineId}/jobs", HttpStatusCode.OK, jobsJson);
    }

    private static GitLabHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenHeadPipelineStatusIsSuccess_ReturnsGreen()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "success" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Green>();
    }

    [Fact]
    public async Task WhenHeadPipelineFailed_NamesTheFailedJobsAsFailingChecks()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(GitLabJobsByPipelineExample.OneFailedJob());
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("rspec:other", "failed", "https://example.com/foo/bar/-/jobs/6")]);
    }

    [Fact]
    public async Task WhenHeadPipelineFailed_ReadsFailedJobsOfThatPipelineInOneRequest()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(GitLabJobsByPipelineExample.OneFailedJob());
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(2);
        HttpRequestMessage jobsRequest = handler.LastRequest.ShouldNotBeNull();
        (jobsRequest.RequestUri!.AbsolutePath, jobsRequest.RequestUri.Query).ShouldBe(
            ("/api/v4/projects/group%2Fproject/pipelines/6/jobs", "?scope[]=failed&per_page=100"));
    }

    [Fact]
    public async Task WhenHeadPipelineFailed_LeavesOutFailedJobsAllowedToFail()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler(GitLabJobsByPipelineExample.FailedJobPlusAllowFailureJob());
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBe([new FailingCheck("rspec:other", "failed", "https://example.com/foo/bar/-/jobs/6")]);
    }

    [Fact]
    public async Task WhenHeadPipelineFailedWithNoFailedJobs_ReturnsRedNamingNothing()
    {
        // Arrange
        FakeHandler handler = FailedPipelineHandler("[]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("running")]
    [InlineData("canceled")]
    [InlineData("manual")]
    public async Task WhenHeadPipelineIsNotFailed_ReadsNoJobs(string pipelineStatus)
    {
        // Arrange
        string json = $$"""{ "iid": 42, "state": "opened", "head_pipeline": { "id": 6, "status": "{{pipelineStatus}}" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenHeadPipelineFailedWithoutId_ReturnsRedNamingNothingAndReadsNoJobs()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "failed" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        ProviderChecks.Red red = success.Value.ShouldBeOfType<ProviderChecks.Red>();
        (red.FailingChecks.Count, handler.AllRequests.Count).ShouldBe((0, 1));
    }

    [Fact]
    public async Task WhenHeadPipelineStatusIsRunning_ReturnsPending()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "running" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Pending>();
    }

    [Fact]
    public async Task WhenHeadPipelineStatusIsCanceled_ReturnsUnfinished()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "canceled" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unfinished>();
    }

    [Fact]
    public async Task WhenHeadPipelineStatusIsManual_ReturnsUnfinished()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "manual" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unfinished>();
    }

    [Fact]
    public async Task WhenHeadPipelineIsNull_ReturnsNoneDetected()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": null }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.NoneDetected>();
    }

    [Fact]
    public async Task WhenHeadPipelineFieldIsAbsent_ReturnsNoneDetected()
    {
        // Arrange — MR response has no head_pipeline key at all
        string json = """{ "iid": 42, "state": "opened" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.NoneDetected>();
    }

    [Fact]
    public async Task WhenGitLabReturns401_ReturnsUnreadable()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert — 401 maps to Unreadable, not a Result failure
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unreadable>();
    }

    [Fact]
    public async Task WhenGitLabReturns403WithoutRateLimitHeader_ReturnsUnreadable()
    {
        // Arrange — 403 without RateLimit-Remaining: 0 is an auth failure, not rate-limit
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Unreadable>();
    }

    [Fact]
    public async Task WhenGitLabReturns403WithRateLimitExhausted_ReturnsRateLimitFailure()
    {
        // Arrange — 403 with RateLimit-Remaining: 0 is a rate-limit failure, not Unreadable
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["RateLimit-Remaining"] = "0";
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderChecks> result = await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ProviderChecks>.Failure failure = result.ShouldBeOfType<Result<ProviderChecks>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectMergeRequestEndpoint()
    {
        // Arrange
        string json = """{ "iid": 42, "state": "opened", "head_pipeline": { "status": "success" } }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetChecksAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/merge_requests/42");
    }
}
