using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.EligibilityViolationTests;

public sealed class FactoryMethods
{
    [Fact]
    public void AllowDirectPushes_SetsExpectedRule()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDirectPushes();

        // Assert
        violation.Rule.ShouldBe(EligibilityViolation.AllowDirectPushesRule);
    }

    [Fact]
    public void AllowForcePushes_SetsExpectedRule()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowForcePushes();

        // Assert
        violation.Rule.ShouldBe(EligibilityViolation.AllowForcePushesRule);
    }

    [Fact]
    public void AllowDeletion_SetsExpectedRule()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.AllowDeletion();

        // Assert
        violation.Rule.ShouldBe(EligibilityViolation.AllowDeletionRule);
    }

    [Fact]
    public void NoCredential_SetsExpectedRule()
    {
        // Arrange
        const string namespaceName = "myorg";

        // Act
        EligibilityViolation violation = EligibilityViolation.NoCredential(namespaceName);

        // Assert
        violation.Rule.ShouldBe($"no-credential:{namespaceName}");
    }

    [Fact]
    public void CredentialUnreadable_SetsExpectedRule()
    {
        // Arrange

        // Act
        EligibilityViolation violation = EligibilityViolation.CredentialUnreadable();

        // Assert
        violation.Rule.ShouldBe(EligibilityViolationInfo.CredentialUnreadableRule);
    }

    [Fact]
    public void CannotPush_SetsExpectedRule()
    {
        // Arrange
        const string slug = "myorg/myrepo";

        // Act
        EligibilityViolation violation = EligibilityViolation.CannotPush(slug);

        // Assert
        violation.Rule.ShouldBe("cannot-push:myorg/myrepo");
    }

    [Fact]
    public void CannotPush_WithRepositorySlug_SetsExpectedRule()
    {
        // Arrange
        RepositorySlug slug = RepositorySlug.Create("myorg/myrepo").ValueOrThrow();

        // Act
        EligibilityViolation violation = EligibilityViolation.CannotPush(slug);

        // Assert
        violation.Rule.ShouldBe("cannot-push:myorg/myrepo");
    }
}
