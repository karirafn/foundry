using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Repositories;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.RepositoryMappingsTests;

public sealed class ToEligibilityInfo
{
    [Fact]
    public void WhenEligibilityIsNull_ReturnsNull()
    {
        // Arrange / Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(null, new WriteProbeVerdict.Granted(), "github");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void WhenEligibilityIsEligible_ReturnsEligibleStatusWithNullReason()
    {
        // Arrange
        RepositoryEligibility.Eligible eligible = new();

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(eligible, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("eligible"),
            () => info.Reason.ShouldBeNull(),
            () => info.Violations.ShouldBeEmpty());
    }

    [Fact]
    public void WhenEligibilityIsIneligibleWithBranchProtectionViolation_ReturnsFixedDescription()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new(
            [EligibilityViolation.AllowDirectPushes()]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("ineligible"),
            () => info.Reason.ShouldBeNull(),
            () => info.Violations.ShouldHaveSingleItem(),
            () => info.Violations[0].Description.ShouldBe(EligibilityViolationInfo.AllowDirectPushesDescription));
    }

    [Fact]
    public void WhenEligibilityIsIneligibleWithCannotPushViolation_GitHubDescription_MentionsTokenPermission()
    {
        // Arrange
        const string slug = "myorg/myrepo";
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.CannotPush(slug)]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.Violations.ShouldHaveSingleItem();
        info.Violations[0].Description.ShouldBe(
            EligibilityViolationInfo.CannotPushDescription(slug, "github"));
    }

    [Fact]
    public void WhenEligibilityIsIneligibleWithCannotPushViolation_GitLabDescription_MentionsDeveloperRole()
    {
        // Arrange
        const string slug = "myorg/myrepo";
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.CannotPush(slug)]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, new WriteProbeVerdict.Granted(), "gitlab");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.Violations.ShouldHaveSingleItem();
        info.Violations[0].Description.ShouldBe(
            EligibilityViolationInfo.CannotPushDescription(slug, "gitlab"));
    }

    [Fact]
    public void WhenEligibilityIsIneligibleWithNoCredentialViolation_DerivesNoCredentialDescription()
    {
        // Arrange
        const string namespaceName = "myorg";
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.NoCredential(namespaceName)]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.Violations.ShouldHaveSingleItem();
        info.Violations[0].Description.ShouldBe(
            EligibilityViolationInfo.NoCredentialDescription(namespaceName));
    }

    [Fact]
    public void WhenEligibilityIsIneligibleWithPackageSourceUnreachableViolation_DerivesTheDescriptionFromTheUrl()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new(
            [EligibilityViolation.PackageSourceUnreachable(new Uri("https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json"))]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, new WriteProbeVerdict.Granted(), "gitlab");

        // Assert
        EligibilityViolationInfo violation = result.ShouldNotBeNull().Violations.ShouldHaveSingleItem();
        violation.ShouldSatisfyAllConditions(
            () => violation.Rule.ShouldBe("package-source-unreachable:https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json"),
            () => violation.Description.ShouldBe(
                "Foundry can't read the package source https://gitlab.com/api/v4/groups/7/-/packages/nuget/index.json."));
    }

    [Fact]
    public void WhenEligibilityIsIneligibleWithNuGetConfigInvalidViolation_ReturnsItsDescription()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.NuGetConfigInvalid()]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, new WriteProbeVerdict.Granted(), "github");

        // Assert
        EligibilityViolationInfo violation = result.ShouldNotBeNull().Violations.ShouldHaveSingleItem();
        violation.ShouldSatisfyAllConditions(
            () => violation.Rule.ShouldBe("nuget-config-invalid"),
            () => violation.Description.ShouldBe("The repository's root nuget.config could not be parsed."));
    }

    [Fact]
    public void WhenEligibilityIsUnreachableWithNeverProbedReason_ReturnsNeverProbedToken()
    {
        // Arrange
        RepositoryEligibility.Unreachable unreachable = new(UnreachableReason.NeverProbed);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(unreachable, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Reason.ShouldBe(RepositoryEligibilityInfo.NeverProbedReason));
    }

    [Fact]
    public void WhenEligibilityIsUnreachableWithRateLimitedReason_ReturnsRateLimitedToken()
    {
        // Arrange
        RepositoryEligibility.Unreachable unreachable = new(UnreachableReason.RateLimited);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(unreachable, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Reason.ShouldBe(RepositoryEligibilityInfo.RateLimitedReason));
    }

    [Fact]
    public void WhenEligibilityIsIneligibleWithUnknownRule_ReturnsNeutralDescription()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new([new EligibilityViolation("unknown-rule:xyz")]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.Violations.ShouldHaveSingleItem();
        info.Violations[0].Description.ShouldBe("This repository is ineligible for an unknown reason.");
    }

    [Fact]
    public void WhenEligibilityIsUnreachableWithBranchRulesUnavailableReason_ReturnsBranchRulesUnavailableToken()
    {
        // Arrange
        RepositoryEligibility.Unreachable unreachable = new(UnreachableReason.BranchRulesUnavailable);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(unreachable, new WriteProbeVerdict.Granted(), "github");

        // Assert
        RepositoryEligibilityInfo info = result.ShouldNotBeNull();
        info.ShouldSatisfyAllConditions(
            () => info.Status.ShouldBe("unreachable"),
            () => info.Reason.ShouldBe(RepositoryEligibilityInfo.BranchRulesUnavailableReason));
    }

    [Fact]
    public void WhenDeniedVerdictNamesGitHubPermissions_CannotPushDescriptionNamesEachLabel()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.CannotPush("myorg/myrepo")]);
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied
        {
            MissingPermissions = ["administration", "repository_hooks", "statuses"],
        };

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, verdict, "github");

        // Assert
        result.ShouldNotBeNull().Violations.ShouldHaveSingleItem().Description.ShouldBe(
            "Token is missing Administration (read and write), Webhooks (read and write) and Commit statuses (read) for myorg/myrepo.");
    }

    [Fact]
    public void WhenDeniedVerdictNamesOneGitHubPermission_CannotPushDescriptionNamesIt()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.CannotPush("myorg/myrepo")]);
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied { MissingPermissions = ["administration"] };

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, verdict, "github");

        // Assert
        result.ShouldNotBeNull().Violations.ShouldHaveSingleItem().Description.ShouldBe(
            "Token is missing Administration (read and write) for myorg/myrepo.");
    }

    [Fact]
    public void WhenDeniedVerdictNamesTheGitLabRole_CannotPushDescriptionNamesTheMaintainerRole()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.CannotPush("myorg/myrepo")]);
        WriteProbeVerdict verdict = new WriteProbeVerdict.Denied { MissingPermissions = ["maintainer_role"] };

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(ineligible, verdict, "gitlab");

        // Assert
        result.ShouldNotBeNull().Violations.ShouldHaveSingleItem().Description.ShouldBe(
            "Token's user needs the Maintainer role on myorg/myrepo.");
    }

    [Fact]
    public void WhenDeniedVerdictHasNoMissingPermissions_CannotPushDescriptionKeepsTheGenericText()
    {
        // Arrange
        RepositoryEligibility.Ineligible ineligible = new([EligibilityViolation.CannotPush("myorg/myrepo")]);

        // Act
        RepositoryEligibilityInfo? result = RepositoryMappings.ToEligibilityInfo(
            ineligible, new WriteProbeVerdict.Denied(), "github");

        // Assert
        result.ShouldNotBeNull().Violations.ShouldHaveSingleItem().Description.ShouldBe(
            "Token lacks push permission or SSO isn't authorized for myorg/myrepo.");
    }
}
