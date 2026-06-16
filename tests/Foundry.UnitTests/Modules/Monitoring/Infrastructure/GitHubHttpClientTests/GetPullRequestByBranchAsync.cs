using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetPullRequestByBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("owner/repo")).Value;

    [Fact]
    public async Task WhenOpenPullRequestExists_ReturnsHtmlUrl()
    {
        // Arrange
        string json = """
            [
              {
                "html_url": "https://github.com/owner/repo/pull/42",
                "number": 42
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBe("https://github.com/owner/repo/pull/42");
    }

    [Fact]
    public async Task WhenNoPullRequestExists_ReturnsEmptyString()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMultiplePullRequestsExist_ReturnsFirstHtmlUrl()
    {
        // Arrange
        string json = """
            [
              { "html_url": "https://github.com/owner/repo/pull/42", "number": 42 },
              { "html_url": "https://github.com/owner/repo/pull/43", "number": 43 }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBe("https://github.com/owner/repo/pull/42");
    }

    [Fact]
    public async Task WhenGitHubReturnsNonSuccess_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            invalidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectPullsEndpointWithHeadFilter()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.GetPullRequestByBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "feat/my-branch",
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/pulls");
        request.RequestUri.Query.ShouldContain("head=owner%3Afeat%2Fmy-branch");
        request.RequestUri.Query.ShouldContain("state=open");
    }
}
