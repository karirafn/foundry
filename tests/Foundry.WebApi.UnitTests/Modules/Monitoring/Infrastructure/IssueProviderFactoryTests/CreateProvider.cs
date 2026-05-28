using System.Net;

using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.WebApi.Modules.Monitoring.Infrastructure;
using Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure.IssueProviderFactoryTests;

public sealed class CreateProvider
{
    private static IIssueProviderFactory BuildSut()
    {
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(httpClient);
        return new IssueProviderFactory(gitHubHttpClient);
    }

    [Fact]
    public void WhenAccountIsGitHubAccount_ReturnsIssueProvider()
    {
        // Arrange
        IIssueProviderFactory sut = BuildSut();
        GitHubAccount account = GitHubAccount.Create("my-account", "GITHUB_TOKEN", new Uri("https://api.github.com"));

        // Act
        IIssueProvider provider = sut.CreateProvider(account, "ghp_token123");

        // Assert
        provider.ShouldNotBeNull();
    }

    [Fact]
    public void WhenAccountTypeIsUnknown_ThrowsNotSupportedException()
    {
        // Arrange
        IIssueProviderFactory sut = BuildSut();
        UnknownAccount account = new();

        // Act & Assert
        Should.Throw<NotSupportedException>(() => sut.CreateProvider(account, "token"));
    }
}
