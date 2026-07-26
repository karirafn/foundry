using Foundry.Modules.Monitoring.Features.Accounts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RequiredScopesTests;

public sealed class For
{
    [Fact]
    public void WhenProviderIsGitHub_ReturnsRepoScope()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("github");

        // Assert
        scopes.ShouldBe(["repo"]);
    }

    [Fact]
    public void WhenProviderIsGitLab_ReturnsApiScope()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("gitlab");

        // Assert
        scopes.ShouldBe(["api"]);
    }

    [Fact]
    public void WhenProviderIsGitHubUppercase_ReturnsRepoScope()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("GitHub");

        // Assert
        scopes.ShouldBe(["repo"]);
    }

    [Fact]
    public void WhenProviderIsGitLabUppercase_ReturnsApiScope()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("GitLab");

        // Assert
        scopes.ShouldBe(["api"]);
    }

    [Fact]
    public void WhenProviderIsUnknown_ReturnsEmpty()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("bitbucket");

        // Assert
        scopes.ShouldBeEmpty();
    }
}
