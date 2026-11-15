// IDE0065: alias placed inside namespace to avoid CS0118 — 'Checks' is also the enclosing namespace segment.
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Checks;

#pragma warning disable IDE0065 // Misplaced using directive
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
#pragma warning restore IDE0065

public sealed class RedIsInfrastructureFailure
{
    private static readonly FailingCheck QuotaExceeded =
        new("build", "ci_quota_exceeded", null, IsInfrastructureFailure: true);

    private static readonly FailingCheck ScriptFailure = new("test", "script_failure", null);

    [Fact]
    public void WhenEveryFailingCheckIsAnInfrastructureFailure_ReturnsTrue()
    {
        // Arrange
        ProviderChecks.Red red = new([QuotaExceeded, QuotaExceeded with { Name = "lint" }]);

        // Act
        bool result = red.IsInfrastructureFailure;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenAFailingCheckRanItsScript_ReturnsFalse()
    {
        // Arrange
        ProviderChecks.Red red = new([QuotaExceeded, ScriptFailure]);

        // Act
        bool result = red.IsInfrastructureFailure;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenNoFailingCheckIsNamed_ReturnsFalse()
    {
        // Arrange
        ProviderChecks.Red red = new([]);

        // Act
        bool result = red.IsInfrastructureFailure;

        // Assert
        result.ShouldBeFalse();
    }
}
