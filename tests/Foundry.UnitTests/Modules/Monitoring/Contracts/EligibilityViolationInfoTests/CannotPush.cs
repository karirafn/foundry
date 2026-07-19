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
    public void CannotPushDescription_FormatsSlugIntoDescription()
    {
        // Arrange
        const string slug = "myorg/myrepo";

        // Act
        string description = EligibilityViolationInfo.CannotPushDescription(slug);

        // Assert
        description.ShouldBe("token cannot push to myorg/myrepo");
    }
}
