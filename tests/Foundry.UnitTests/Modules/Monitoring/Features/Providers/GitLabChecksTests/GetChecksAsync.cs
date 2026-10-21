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
    public async Task WhenHeadPipelineStatusIsFailed_ReturnsRed()
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
        result.IsSuccess.ShouldBeTrue();
        Result<ProviderChecks>.Success success = result.ShouldBeOfType<Result<ProviderChecks>.Success>();
        success.Value.ShouldBeOfType<ProviderChecks.Red>();
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
