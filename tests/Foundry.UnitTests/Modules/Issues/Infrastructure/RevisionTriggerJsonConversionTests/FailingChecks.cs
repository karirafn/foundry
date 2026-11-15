using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Infrastructure.Configurations;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Infrastructure.RevisionTriggerJsonConversionTests;

public sealed class FailingChecks
{
    private static string Serialize(RevisionTrigger trigger) =>
        (string)RevisionTriggerJsonConversion.Converter.ConvertToProvider(trigger)!;

    private static RevisionTrigger Deserialize(string json) =>
        (RevisionTrigger)RevisionTriggerJsonConversion.Converter.ConvertFromProvider(json)!;

    [Fact]
    public void AFailedChecksTrigger_RoundTripsWhetherEachCheckIsAnInfrastructureFailure()
    {
        // Arrange
        IReadOnlyList<FailingCheck> checks =
        [
            new FailingCheck("build", "ci_quota_exceeded", "https://gitlab.com/group/project/-/jobs/1", IsInfrastructureFailure: true),
            new FailingCheck("test", "script_failure", null),
        ];

        // Act
        RevisionTrigger read = Deserialize(Serialize(new RevisionTrigger.FailedChecks(checks)));

        // Assert
        read.ShouldBeOfType<RevisionTrigger.FailedChecks>().FailingChecks.ShouldBe(checks);
    }

    [Fact]
    public void AnInfrastructureFailure_IsWrittenAsTheInfrastructureProperty()
    {
        // Arrange
        RevisionTrigger trigger = new RevisionTrigger.FailedChecks(
            [new FailingCheck("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true)]);

        // Act
        string json = Serialize(trigger);

        // Assert
        json.ShouldContain("\"infrastructure\":true");
    }

    [Fact]
    public void AFailingCheckWrittenWithoutTheInfrastructureProperty_ReadsAsFixable()
    {
        // Arrange
        const string json = """
            {"discriminator":"failed_checks","comments":null,"omittedCommentCount":null,"newestConsumedCommentAt":null,"failingChecks":[{"name":"build","conclusion":"failed","detailsUrl":null}]}
            """;

        // Act
        RevisionTrigger read = Deserialize(json);

        // Assert
        read.ShouldBeOfType<RevisionTrigger.FailedChecks>().FailingChecks
            .ShouldHaveSingleItem().IsInfrastructureFailure.ShouldBeFalse();
    }
}
