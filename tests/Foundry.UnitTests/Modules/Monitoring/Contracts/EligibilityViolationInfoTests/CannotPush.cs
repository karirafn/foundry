using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.EligibilityViolationInfoTests;

public sealed class CannotPush
{
    [Fact]
    public void CannotPushRule_FormatsSlugIntoRule()
    {
        // Arrange
        const string slug = "myorg/myrepo";

        // Act
        string rule = EligibilityViolationInfo.CannotPushRule(slug);

        // Assert
        rule.ShouldBe("cannot-push:myorg/myrepo");
    }

    [Fact]
    public void WhenProviderIsGitHub_CannotPushDescriptionMentionsTokenPermissionAndSso()
    {
        // Arrange
        const string slug = "myorg/myrepo";

        // Act
        string description = EligibilityViolationInfo.CannotPushDescription(slug, "github");

        // Assert
        description.ShouldBe(
            "Token lacks push permission or SSO isn't authorized for myorg/myrepo.");
    }

    [Fact]
    public void WhenProviderIsGitLab_CannotPushDescriptionMentionsRoleRequirement()
    {
        // Arrange
        const string slug = "myorg/myrepo";

        // Act
        string description = EligibilityViolationInfo.CannotPushDescription(slug, "gitlab");

        // Assert
        description.ShouldBe(
            "Token's role is below Developer for myorg/myrepo.");
    }

    [Fact]
    public void WhenProviderIsUnknown_CannotPushDescriptionUsesGenericWording()
    {
        // Arrange
        const string slug = "myorg/myrepo";

        // Act
        string description = EligibilityViolationInfo.CannotPushDescription(slug, "unknown");

        // Assert
        description.ShouldBe(
            "Token cannot push for myorg/myrepo.");
    }

    [Fact]
    public void WhenProviderIsGitHub_NoPushAccessExplanationMentionsTokenPermissionAndSso()
    {
        // Act
        string explanation = EligibilityViolationInfo.NoPushAccessExplanation("github");

        // Assert
        explanation.ShouldBe(
            "Your token lacks push permission or SSO isn't authorized.");
    }

    [Fact]
    public void WhenProviderIsGitLab_NoPushAccessExplanationMentionsRoleRequirement()
    {
        // Act
        string explanation = EligibilityViolationInfo.NoPushAccessExplanation("gitlab");

        // Assert
        explanation.ShouldBe(
            "Your token's role is below Developer.");
    }

    [Fact]
    public void CredentialUnreadableRule_IsConstant()
    {
        // Arrange

        // Act
        string rule = EligibilityViolationInfo.CredentialUnreadableRule;

        // Assert
        rule.ShouldBe("credential-unreadable");
    }

    [Fact]
    public void CredentialUnreadableDescription_IsSentenceCaseAndPeriodTerminated()
    {
        // Arrange

        // Act
        string description = EligibilityViolationInfo.CredentialUnreadableDescription;

        // Assert
        description.ShouldBe(
            "The account token can't be decrypted. Re-enter it on the account to resume.");
    }

    [Fact]
    public void NoCredentialDescription_IsSentenceCaseAndPeriodTerminated()
    {
        // Arrange
        const string namespaceName = "myorg";

        // Act
        string description = EligibilityViolationInfo.NoCredentialDescription(namespaceName);

        // Assert
        description.ShouldBe("No credential for namespace myorg.");
    }
}
