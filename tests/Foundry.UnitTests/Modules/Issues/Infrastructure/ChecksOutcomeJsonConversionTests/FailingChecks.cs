using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.ChecksOutcomeJsonConversionTests;

public sealed class FailingChecks
{
    private static string Serialize(ChecksOutcome outcome) =>
        (string)ChecksOutcomeJsonConversion.Converter.ConvertToProvider(outcome)!;

    private static ChecksOutcome Deserialize(string json) =>
        (ChecksOutcome)ChecksOutcomeJsonConversion.Converter.ConvertFromProvider(json)!;

    [Fact]
    public void AStillFailingOutcome_RoundTripsWhetherEachCheckIsAnInfrastructureFailure()
    {
        // Arrange
        IReadOnlyList<FailingCheck> checks =
        [
            new FailingCheck("build", "ci_quota_exceeded", "https://gitlab.com/group/project/-/jobs/1", IsInfrastructureFailure: true),
            new FailingCheck("test", "script_failure", null),
        ];

        // Act
        ChecksOutcome read = Deserialize(Serialize(new ChecksOutcome.StillFailing(checks)));

        // Assert
        read.ShouldBeOfType<ChecksOutcome.StillFailing>().FailingChecks.ShouldBe(checks);
    }

    [Fact]
    public void AnInfrastructureFailedOutcome_RoundTripsWithItsChecks()
    {
        // Arrange
        IReadOnlyList<FailingCheck> checks =
        [
            new FailingCheck("build", "ci_quota_exceeded", "https://gitlab.com/group/project/-/jobs/1", IsInfrastructureFailure: true),
            new FailingCheck("test", "runner_system_failure", null, IsInfrastructureFailure: true),
        ];

        // Act
        ChecksOutcome read = Deserialize(Serialize(new ChecksOutcome.InfrastructureFailed(checks)));

        // Assert
        read.ShouldBeOfType<ChecksOutcome.InfrastructureFailed>().FailingChecks.ShouldBe(checks);
    }

    [Fact]
    public void InfrastructureFailedOutcomesWithDifferentChecks_AreNotEqualToTheComparer()
    {
        // Arrange
        ChecksOutcome a = new ChecksOutcome.InfrastructureFailed(
            [new FailingCheck("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true)]);
        ChecksOutcome b = new ChecksOutcome.InfrastructureFailed(
            [new FailingCheck("test", "ci_quota_exceeded", null, IsInfrastructureFailure: true)]);

        // Act
        bool equal = ChecksOutcomeJsonConversion.Comparer.Equals(a, b);

        // Assert
        equal.ShouldBeFalse();
    }

    [Fact]
    public void AnInfrastructureFailure_IsWrittenAsTheInfrastructureProperty()
    {
        // Arrange
        ChecksOutcome outcome = new ChecksOutcome.StillFailing(
            [new FailingCheck("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true)]);

        // Act
        string json = Serialize(outcome);

        // Assert
        json.ShouldContain("\"infrastructure\":true");
    }

    [Fact]
    public void AFailingCheckWrittenWithoutTheInfrastructureProperty_ReadsAsFixable()
    {
        // Arrange
        const string json = """
            {"discriminator":"still_failing","failingChecks":[{"name":"build","conclusion":"failed","detailsUrl":null}]}
            """;

        // Act
        ChecksOutcome read = Deserialize(json);

        // Assert
        read.ShouldBeOfType<ChecksOutcome.StillFailing>().FailingChecks
            .ShouldHaveSingleItem().IsInfrastructureFailure.ShouldBeFalse();
    }
}
