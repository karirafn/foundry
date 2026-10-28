using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.ChecksOutcomeTests;

public sealed class Discriminator
{
    [Fact]
    public void WhenPassed_DiscriminatorIsPassed()
    {
        // Arrange
        ChecksOutcome outcome = new ChecksOutcome.Passed();

        // Act
        string discriminator = outcome.Discriminator;

        // Assert
        discriminator.ShouldBe("passed");
    }

    [Fact]
    public void WhenNoneDetected_DiscriminatorIsNoneDetected()
    {
        // Arrange
        ChecksOutcome outcome = new ChecksOutcome.NoneDetected();

        // Act
        string discriminator = outcome.Discriminator;

        // Assert
        discriminator.ShouldBe("none_detected");
    }

    [Fact]
    public void WhenUnfinished_DiscriminatorIsUnfinished()
    {
        // Arrange
        ChecksOutcome outcome = new ChecksOutcome.Unfinished();

        // Act
        string discriminator = outcome.Discriminator;

        // Assert
        discriminator.ShouldBe("unfinished");
    }

    [Fact]
    public void WhenStillFailing_DiscriminatorIsStillFailing()
    {
        // Arrange
        FailingCheck check = new(Name: "build", Conclusion: "failure", DetailsUrl: null);
        ChecksOutcome outcome = new ChecksOutcome.StillFailing(FailingChecks: [check]);

        // Act
        string discriminator = outcome.Discriminator;

        // Assert
        discriminator.ShouldBe("still_failing");
    }

    [Fact]
    public void WhenUnreadable_DiscriminatorIsUnreadable()
    {
        // Arrange
        ChecksOutcome outcome = new ChecksOutcome.Unreadable();

        // Act
        string discriminator = outcome.Discriminator;

        // Assert
        discriminator.ShouldBe("unreadable");
    }

    [Fact]
    public void WhenStillFailing_ExposesFailingChecks()
    {
        // Arrange
        FailingCheck check = new(Name: "build", Conclusion: "failure", DetailsUrl: "https://ci.example.com/builds/1");
        ChecksOutcome.StillFailing outcome = new(FailingChecks: [check]);

        // Act
        IReadOnlyList<FailingCheck> failingChecks = outcome.FailingChecks;

        // Assert
        failingChecks.ShouldSatisfyAllConditions(
            () => failingChecks.Count.ShouldBe(1),
            () => failingChecks[0].Name.ShouldBe("build"),
            () => failingChecks[0].Conclusion.ShouldBe("failure"),
            () => failingChecks[0].DetailsUrl.ShouldBe("https://ci.example.com/builds/1"));
    }
}
