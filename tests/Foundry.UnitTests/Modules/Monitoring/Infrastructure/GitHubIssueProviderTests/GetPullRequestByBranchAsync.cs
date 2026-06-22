using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class GetPullRequestByBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(SequentialFakeHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        return new GitHubIssueProvider(gitHubHttpClient, ValidToken, ValidBaseUrl);
    }

    [Fact]
    public async Task WhenPullRequestExists_ReturnsPrUrl()
    {
        // Arrange
        string pullsJson = """
            [
              { "html_url": "https://github.com/owner/repo/pull/42", "number": 42 }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, pullsJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        GitHubIssueProvider sut = new(gitHubHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
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
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        GitHubIssueProvider sut = new(gitHubHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenApiReturnsFails_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        GitHubIssueProvider sut = new(gitHubHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
