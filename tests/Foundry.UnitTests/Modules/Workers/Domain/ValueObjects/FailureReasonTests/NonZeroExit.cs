using System.Text.Json;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class NonZeroExit
{
    [Fact]
    public void WhenConstructed_ExitCodeIsPreserved()
    {
        // Arrange

        // Act
        FailureReason.NonZeroExit reason = new(ExitCode: 1);

        // Assert
        reason.ExitCode.ShouldBe(1);
    }

    [Fact]
    public void WhenTwoInstancesHaveSameExitCode_AreEqual()
    {
        // Arrange
        FailureReason.NonZeroExit a = new(ExitCode: 2);
        FailureReason.NonZeroExit b = new(ExitCode: 2);

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentExitCode_AreNotEqual()
    {
        // Arrange
        FailureReason.NonZeroExit a = new(ExitCode: 1);
        FailureReason.NonZeroExit b = new(ExitCode: 2);

        // Act

        // Assert
        a.ShouldNotBe(b);
    }

    [Fact]
    public void WhenPersistedWithoutDetail_DeserialisesWithNoDetailAndUnchangedSummary()
    {
        // Arrange — the JSON a NonZeroExit reason was persisted as before it carried a detail
        const string persisted = """{"$type":"non_zero_exit","ExitCode":1}""";

        // Act
        FailureReason? reason = JsonSerializer.Deserialize<FailureReason>(persisted);

        // Assert
        FailureReason.NonZeroExit nonZeroExit = reason.ShouldBeOfType<FailureReason.NonZeroExit>();
        nonZeroExit.ShouldSatisfyAllConditions(
            () => nonZeroExit.ExitCode.ShouldBe(1),
            () => nonZeroExit.Detail.ShouldBeNull(),
            () => nonZeroExit.Summary.ShouldBe("Non-zero exit code: 1"));
    }

    [Fact]
    public void WhenSerialisedWithDetail_RoundTripsDetail()
    {
        // Arrange
        FailureReason reason = new FailureReason.NonZeroExit(ExitCode: 1, Detail: "API Error: 400");

        // Act
        FailureReason? roundTripped = JsonSerializer.Deserialize<FailureReason>(JsonSerializer.Serialize(reason));

        // Assert
        roundTripped.ShouldBe(reason);
    }
}
