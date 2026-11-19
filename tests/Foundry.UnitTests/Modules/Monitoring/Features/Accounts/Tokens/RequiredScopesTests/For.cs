using Foundry.Modules.Monitoring.Features.Accounts.Tokens;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Tokens.RequiredScopesTests;

public sealed class For
{
    [Fact]
    public void WhenProviderIsGitHub_ReturnsFineGrainedPermissionLabels()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("github");

        // Assert
        scopes.ShouldBe([
            "Contents (read and write)",
            "Issues (read and write)",
            "Pull requests (read and write)",
            "Administration (read and write)",
            "Webhooks (read and write)",
            "Commit statuses (read)",
            "Actions (read)",
            "Metadata (read)",
            "Workflows (read and write)"]);
    }

    [Fact]
    public void WhenProviderIsGitLab_ReturnsApiScopeAndMaintainerRole()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("gitlab");

        // Assert
        scopes.ShouldBe(["api", "Maintainer role"]);
    }

    [Fact]
    public void WhenProviderIsGitHubUppercase_ReturnsFineGrainedPermissionLabels()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("GitHub");

        // Assert
        scopes.ShouldBe([
            "Contents (read and write)",
            "Issues (read and write)",
            "Pull requests (read and write)",
            "Administration (read and write)",
            "Webhooks (read and write)",
            "Commit statuses (read)",
            "Actions (read)",
            "Metadata (read)",
            "Workflows (read and write)"]);
    }

    [Fact]
    public void WhenProviderIsGitLabUppercase_ReturnsApiScopeAndMaintainerRole()
    {
        // Arrange

        // Act
        IReadOnlyList<string> scopes = RequiredScopes.For("GitLab");

        // Assert
        scopes.ShouldBe(["api", "Maintainer role"]);
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
