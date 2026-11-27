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
    public void WhenProviderIsGitLab_ReturnsTheFineGrainedTokenGroupedByGrantLevel()
    {
        // Arrange

        // Act
        TokenRequirements? result = TokenRequirementsCatalog.For("gitlab");

        // Assert
        result.ShouldNotBeNull();
        result.ShouldSatisfyAllConditions(
            () => result.ProviderType.ShouldBe("gitlab"),
            () => result.TokenTypeLabel.ShouldBe("GitLab fine-grained personal access token"),
            () => result.Permissions.ShouldBe([
                new TokenPermissionRequirement("All groups and projects I have access to, or only selected projects (select each monitored project individually)", "granted", "Group and project access"),
                new TokenPermissionRequirement("Project: Read", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Work Item (Project Planning): Read, Create", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Merge Request (Repository): Read, Create, Update, Merge", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Pipeline (CI/CD): Read", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Job (CI/CD): Read, Run", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Webhook (Integrations): Read, Create, Update, Delete, Trigger", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Branch: Read, Create, Delete, Protect", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Protected Branch: Read, Update", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Repository: Read", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Commit: Read", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Code: Download, Push", "granted", "On each selected project, or on all groups and projects"),
                new TokenPermissionRequirement("Personal Access Token (System Access): Read", "granted", "On your user"),
                new TokenPermissionRequirement("User (System Access): Read", "granted", "On your user"),
                new TokenPermissionRequirement("Project: Read", "granted", "On your user, only with all groups and projects"),
                new TokenPermissionRequirement("Package (Packages and Registry): Read", "granted", "Optional, for a GitLab-hosted NuGet feed"),
                new TokenPermissionRequirement("Maintainer role", "role", "Repository role")]),
            () => result.CreationUrlTemplate.ShouldBe("{baseUrl}/-/user_settings/personal_access_tokens"),
            () => result.CreationNote.ShouldBe("Requires GitLab 19.5 or later. On the page, choose Generate token, then Fine-grained token."),
            () => result.ClosingNote.ShouldBe("Foundry rejects a GitLab token that grants anything else, or that reaches only your personal projects or the whole instance."));
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
