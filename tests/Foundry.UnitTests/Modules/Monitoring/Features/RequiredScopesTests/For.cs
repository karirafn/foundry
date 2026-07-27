using Foundry.Modules.Monitoring.Features.Accounts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.RequiredScopesTests;

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
            "Workflows (write)",
            "Metadata (read)"]);
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
            "Workflows (write)",
            "Metadata (read)"]);
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
