using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.IssueProviderFactoryTests;

public sealed class CreateProvider
{
    private static IIssueProviderFactory BuildSut()
    {
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        GitLabHttpClient gitLabHttpClient = new(httpClient);
        return new IssueProviderFactory(gitHubHttpClient, gitLabHttpClient);
    }

    [Fact]
    public void WhenAccountIsGitHubAccount_ReturnsIssueProvider()
    {
        // Arrange
        IIssueProviderFactory sut = BuildSut();
        GitHubAccount account = GitHubAccount.Create("my-account", "GITHUB_TOKEN", new Uri("https://github.com"));

        // Act
        IIssueProvider provider = sut.CreateProvider(account, "ghp_token123");

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void WhenAccountIsGitLabAccount_ReturnsIssueProvider()
    {
        // Arrange
        IIssueProviderFactory sut = BuildSut();
        GitLabAccount account = GitLabAccount.Create("my-gitlab", "glpat_token", new Uri("https://gitlab.com"));

        // Act
        IIssueProvider provider = sut.CreateProvider(account, "glpat_token123");

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void WhenAccountTypeIsUnknown_ThrowsNotSupportedException()
    {
        // Arrange
        IIssueProviderFactory sut = BuildSut();
        UnknownAccount account = new();

        // Act
        Action act = () => sut.CreateProvider(account, "token");

        // Assert
        Should.Throw<NotSupportedException>(act);
    }
}
