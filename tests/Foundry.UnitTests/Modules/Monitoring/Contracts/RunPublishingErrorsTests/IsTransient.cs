using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.RunPublishingErrorsTests;

public sealed class IsTransient
{
    [Fact]
    public void ATransientError_IsTransient()
    {
        // Arrange
        Error error = RunPublishingErrors.Transient("returned 502");

        // Act
        bool transient = RunPublishingErrors.IsTransient(error);

        // Assert
        transient.ShouldBeTrue();
    }

    [Fact]
    public void ARefusedError_IsNotTransient()
    {
        // Arrange
        Error error = RunPublishingErrors.Refused("returned 403");

        // Act
        bool transient = RunPublishingErrors.IsTransient(error);

        // Assert
        transient.ShouldBeFalse();
    }
}
