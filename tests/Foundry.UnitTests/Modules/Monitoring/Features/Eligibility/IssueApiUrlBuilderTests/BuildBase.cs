using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Eligibility;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Eligibility.IssueApiUrlBuilderTests;

public sealed class BuildBase
{
    [Fact]
    public void WhenGitHubCom_ReturnsApiGitHubComReposUrl()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://github.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("Test", "token", baseUrl);
        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();

        // Act
        string result = IssueApiUrlBuilder.BuildBase(credential, slug);

        // Assert
        result.ShouldBe("https://api.github.com/repos/owner/repo/issues");
    }

    [Fact]
    public void WhenGitHubEnterpriseServer_ReturnsApiV3ReposUrl()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://ghe.example.com").ValueOrThrow();
        GitHubCredential credential = GitHubCredential.Create("Test", "token", baseUrl);
        RepositorySlug slug = RepositorySlug.Create("org/project").ValueOrThrow();

        // Act
        string result = IssueApiUrlBuilder.BuildBase(credential, slug);

        // Assert
        result.ShouldBe("https://ghe.example.com/api/v3/repos/org/project/issues");
    }

    [Fact]
    public void WhenGitLabCom_ReturnsApiV4ProjectsUrl()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitLabCredential credential = GitLabCredential.Create("Test", "token", baseUrl);
        RepositorySlug slug = RepositorySlug.Create("owner/repo").ValueOrThrow();

        // Act
        string result = IssueApiUrlBuilder.BuildBase(credential, slug);

        // Assert
        result.ShouldBe("https://gitlab.com/api/v4/projects/owner%2Frepo/issues");
    }

    [Fact]
    public void WhenNestedGitLabGroup_EncodesFullPathAndReturnsCorrectUrl()
    {
        // Arrange
        BaseUrl baseUrl = BaseUrl.Create("https://gitlab.com").ValueOrThrow();
        GitLabCredential credential = GitLabCredential.Create("Test", "token", baseUrl);
        RepositorySlug slug = RepositorySlug.Create("group/sub/project").ValueOrThrow();

        // Act
        string result = IssueApiUrlBuilder.BuildBase(credential, slug);

        // Assert
        result.ShouldBe("https://gitlab.com/api/v4/projects/group%2Fsub%2Fproject/issues");
    }
}
