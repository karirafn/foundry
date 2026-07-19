using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Testing;
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
    public void WhenAccountIsGitHubCredential_ReturnsIssueProvider()
    {
        // Arrange
        IIssueProviderFactory sut = BuildSut();
        GitHubCredential credential = GitHubCredential.Create("my-account", "GITHUB_TOKEN", BaseUrl.Create("https://github.com").ValueOrThrow());

        // Act
        IIssueProvider provider = sut.CreateProvider(credential, "ghp_token123");

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void WhenAccountIsGitLabCredential_ReturnsIssueProvider()
    {
        // Arrange
        IIssueProviderFactory sut = BuildSut();
        GitLabCredential credential = GitLabCredential.Create("my-gitlab", "glpat_token", BaseUrl.Create("https://gitlab.com").ValueOrThrow());

        // Act
        IIssueProvider provider = sut.CreateProvider(credential, "glpat_token123");

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void WhenCredentialTypeIsUnknown_ThrowsNotSupportedException()
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
