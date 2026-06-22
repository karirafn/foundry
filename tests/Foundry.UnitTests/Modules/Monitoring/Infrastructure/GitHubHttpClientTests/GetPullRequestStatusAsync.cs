using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetPullRequestStatusAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenPrIsMerged_ReturnsMergedAndClosedStatus()
    {
        // Arrange
        string json = """{ "number": 123, "state": "closed", "merged": true, "merged_at": "2026-05-01T00:00:00Z" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsMerged.ShouldBeTrue(),
            () => success.Value.IsClosed.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenPrIsClosedWithoutMerge_ReturnsClosedNotMerged()
    {
        // Arrange
        string json = """{ "number": 123, "state": "closed", "merged": false, "merged_at": null }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsClosed.ShouldBeTrue(),
            () => success.Value.IsMerged.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenPrIsOpen_ReturnsNotClosedNotMerged()
    {
        // Arrange
        string json = """{ "number": 123, "state": "open", "merged": false, "merged_at": null }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsClosed.ShouldBeFalse(),
            () => success.Value.IsMerged.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenPrUrlCannotBeParsed_ReturnsInvalidUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/issues/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidPullRequestUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        string json = """{ "number": 123, "state": "open", "merged": false, "merged_at": null }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/pulls/123");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitExhausted_ReturnsRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsInvalidBaseUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            invalidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }
}
