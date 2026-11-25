using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Accounts.Tokens.TokenRequirementsCatalogTests;

public sealed class For
{
    [Fact]
    public void WhenProviderIsGitHub_ReturnsFineGrainedLabelAndPermissionsAndCreationTemplate()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("github");

        // Assert
        result.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.ProviderType.ShouldBe("github"),
            () => result.TokenTypeLabel.ShouldBe("GitHub fine-grained personal access token"),
            () => result.Permissions.ShouldBe([
                new TokenPermissionRequirement("Repository access: Only select repositories", "not-checked"),
                new TokenPermissionRequirement("Contents (read and write)", "probe"),
                new TokenPermissionRequirement("Issues (read and write)", "probe"),
                new TokenPermissionRequirement("Pull requests (read and write)", "probe"),
                new TokenPermissionRequirement("Administration (read and write)", "probe"),
                new TokenPermissionRequirement("Webhooks (read and write)", "probe"),
                new TokenPermissionRequirement("Commit statuses (read)", "probe"),
                new TokenPermissionRequirement("Actions (read and write)", "probe"),
                new TokenPermissionRequirement("Metadata (read)", "always-granted"),
                new TokenPermissionRequirement("Workflows (read and write)", "unverifiable")]),
            () => result.CreationUrlTemplate.ShouldBe(
                "{baseUrl}/settings/personal-access-tokens/new?name=Foundry&contents=write&issues=write&pull_requests=write&administration=write&repository_hooks=write&statuses=read&actions=write&workflows=write"),
            () => result.ClosingNote.ShouldBe(
                "GitHub can't report a token's extra permissions, so Foundry can't check for any. Grant only the permissions listed here."),
            () => result.CreationNote.ShouldBeNull());
    }

    [Fact]
    public void WhenProviderIsGitLab_ReturnsPersonalAccessTokenLabelAndApiScopeAndGitLabTemplate()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("gitlab");

        // Assert
        result.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.ProviderType.ShouldBe("gitlab"),
            () => result.TokenTypeLabel.ShouldBe("GitLab personal access token"),
            () => result.Permissions.ShouldBe([
                new TokenPermissionRequirement("api", "scope"),
                new TokenPermissionRequirement("Maintainer role", "role")]),
            () => result.CreationUrlTemplate.ShouldBe("{baseUrl}/-/user_settings/personal_access_tokens"));
    }

    [Fact]
    public void WhenProviderIsUnknown_ReturnsNull()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("bitbucket");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenProviderIsGitHub_ReturnsResourceOwnerHintForOrganizations()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("github");

        // Assert
        result.ShouldNotBeNull();
        result.ResourceOwnerHint.ShouldBe(
            "Fine-grained tokens are bound to a single resource owner. To reach an organization's repositories, choose that organization as the token's resource owner when creating the token.");
    }

    [Fact]
    public void WhenProviderIsGitLab_ReturnsNullResourceOwnerHint()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("gitlab");

        // Assert
        result.ShouldNotBeNull();
        result.ResourceOwnerHint.ShouldBeNull();
    }

    [Fact]
    public void WhenProviderIsGitHubUppercase_ReturnsResult()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("GitHub");

        // Assert
        result.ShouldNotBeNull();
        result.ProviderType.ShouldBe("github");
    }

    [Fact]
    public void WhenProviderIsGitLabUppercase_ReturnsResult()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("GitLab");

        // Assert
        result.ShouldNotBeNull();
        result.ProviderType.ShouldBe("gitlab");
    }
}
