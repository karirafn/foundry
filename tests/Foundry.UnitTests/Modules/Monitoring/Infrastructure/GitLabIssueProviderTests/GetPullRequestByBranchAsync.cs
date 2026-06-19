using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabIssueProviderTests;

public sealed class GetPullRequestByBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat_token";

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("group/project")).Value;

    [Fact]
    public async Task WhenMergeRequestExists_ReturnsMrUrl()
    {
        // Arrange
        string mrJson = """
            [
              { "web_url": "https://gitlab.com/group/project/-/merge_requests/42" }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, mrJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient);
        GitLabIssueProvider sut = new(gitLabHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBe("https://gitlab.com/group/project/-/merge_requests/42");
    }

    [Fact]
    public async Task WhenNoMergeRequestExists_ReturnsEmptyString()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient gitLabHttpClient = new(httpClient);
        GitLabIssueProvider sut = new(gitLabHttpClient, ValidToken, ValidBaseUrl);

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
        GitLabHttpClient gitLabHttpClient = new(httpClient);
        GitLabIssueProvider sut = new(gitLabHttpClient, ValidToken, ValidBaseUrl);

        // Act
        Result<string> result = await sut.GetPullRequestByBranchAsync(
            ValidSlug,
            "feat/my-branch",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
    }
}
