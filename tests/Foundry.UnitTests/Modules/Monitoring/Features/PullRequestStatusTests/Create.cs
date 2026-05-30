using Foundry.Modules.Monitoring.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PullRequestStatusTests;

public sealed class Create
{
    [Fact]
    public void WhenCreatedClosed_HasExpectedState()
    {
        // Arrange / Act
        PullRequestStatus status = new(IsClosed: true, IsMerged: false);

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.IsClosed.ShouldBeTrue(),
            () => status.IsMerged.ShouldBeFalse());
    }

    [Fact]
    public void WhenCreatedMerged_HasExpectedState()
    {
        // Arrange / Act
        PullRequestStatus status = new(IsClosed: true, IsMerged: true);

        // Assert
        status.ShouldSatisfyAllConditions(
            () => status.IsClosed.ShouldBeTrue(),
            () => status.IsMerged.ShouldBeTrue());
    }
}
