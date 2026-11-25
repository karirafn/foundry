using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Infrastructure.Configurations;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.RevisionTriggerJsonConversionTests;

public sealed class SpecChanged
{
    private static string Serialize(RevisionTrigger trigger) =>
        (string)RevisionTriggerJsonConversion.Converter.ConvertToProvider(trigger)!;

    private static RevisionTrigger Deserialize(string json) =>
        (RevisionTrigger)RevisionTriggerJsonConversion.Converter.ConvertFromProvider(json)!;

    [Fact]
    public void ASpecChangedTrigger_RoundTripsThroughTheStoredColumn()
    {
        // Act
        RevisionTrigger read = Deserialize(Serialize(new RevisionTrigger.SpecChanged()));

        // Assert
        read.ShouldBeOfType<RevisionTrigger.SpecChanged>();
    }

    [Fact]
    public void ASpecChangedTrigger_IsStoredUnderItsDiscriminator()
    {
        // Act
        string json = Serialize(new RevisionTrigger.SpecChanged());

        // Assert
        json.ShouldContain("\"discriminator\":\"spec_changed\"");
    }

    [Fact]
    public void TwoSpecChangedTriggers_CompareEqual()
    {
        // Act
        bool equal = RevisionTriggerJsonConversion.Comparer.Equals(
            new RevisionTrigger.SpecChanged(),
            new RevisionTrigger.SpecChanged());

        // Assert
        equal.ShouldBeTrue();
    }
}
