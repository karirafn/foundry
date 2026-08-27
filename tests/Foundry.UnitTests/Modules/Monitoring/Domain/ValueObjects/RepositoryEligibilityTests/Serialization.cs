using System.Text.Json;

using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.RepositoryEligibilityTests;

public sealed class Serialization
{
    private static readonly JsonSerializerOptions Options = new();

    [Fact]
    public void WhenUnreachableWithNeverProbedReason_RoundTripsReason()
    {
        // Arrange
        RepositoryEligibility eligibility = new RepositoryEligibility.Unreachable(UnreachableReason.NeverProbed);

        // Act
        string json = JsonSerializer.Serialize(eligibility, Options);
        RepositoryEligibility? deserialized = JsonSerializer.Deserialize<RepositoryEligibility>(json, Options);

        // Assert
        RepositoryEligibility.Unreachable unreachable = deserialized.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.NeverProbed);
    }

    [Fact]
    public void WhenUnreachableWithRateLimitedReason_RoundTripsReason()
    {
        // Arrange
        RepositoryEligibility eligibility = new RepositoryEligibility.Unreachable(UnreachableReason.RateLimited);

        // Act
        string json = JsonSerializer.Serialize(eligibility, Options);
        RepositoryEligibility? deserialized = JsonSerializer.Deserialize<RepositoryEligibility>(json, Options);

        // Assert
        RepositoryEligibility.Unreachable unreachable = deserialized.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.RateLimited);
    }

    [Fact]
    public void WhenUnreachableWithBranchRulesUnavailableReason_RoundTripsReason()
    {
        // Arrange
        RepositoryEligibility eligibility = new RepositoryEligibility.Unreachable(UnreachableReason.BranchRulesUnavailable);

        // Act
        string json = JsonSerializer.Serialize(eligibility, Options);
        RepositoryEligibility? deserialized = JsonSerializer.Deserialize<RepositoryEligibility>(json, Options);

        // Assert
        RepositoryEligibility.Unreachable unreachable = deserialized.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.BranchRulesUnavailable);
    }

    [Fact]
    public void WhenUnreachableLegacyJsonWithoutReasonField_DeserializesToNeverProbed()
    {
        // Arrange — legacy rows persisted before the Reason field was added deserialize to
        // NeverProbed, the same behavior as a freshly-created unprobed repository.
        const string legacyJson = """{"$type":"unreachable"}""";

        // Act
        RepositoryEligibility? deserialized = JsonSerializer.Deserialize<RepositoryEligibility>(legacyJson, Options);

        // Assert
        RepositoryEligibility.Unreachable unreachable = deserialized.ShouldBeOfType<RepositoryEligibility.Unreachable>();
        unreachable.Reason.ShouldBe(UnreachableReason.NeverProbed);
    }

    [Fact]
    public void WhenUnreachableDefaultConstruct_ReasonIsNeverProbed()
    {
        // Arrange / Act
        RepositoryEligibility.Unreachable unreachable = new RepositoryEligibility.Unreachable();

        // Assert
        unreachable.Reason.ShouldBe(UnreachableReason.NeverProbed);
    }

    [Fact]
    public void WhenEligibleSerialized_RoundTripsToEligible()
    {
        // Arrange
        RepositoryEligibility eligibility = new RepositoryEligibility.Eligible();

        // Act
        string json = JsonSerializer.Serialize(eligibility, Options);
        RepositoryEligibility? deserialized = JsonSerializer.Deserialize<RepositoryEligibility>(json, Options);

        // Assert
        deserialized.ShouldBeOfType<RepositoryEligibility.Eligible>();
    }
}
