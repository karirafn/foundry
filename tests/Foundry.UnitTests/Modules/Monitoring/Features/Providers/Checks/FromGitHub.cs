// IDE0065: alias placed inside namespace to avoid CS0118 — 'Checks' is also the enclosing namespace segment.
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Checks;

#pragma warning disable IDE0065 // Misplaced using directive
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
#pragma warning restore IDE0065

public sealed class FromGitHub
{
    [Fact]
    public void WhenRollupStateIsNull_ReturnsNoneDetected()
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitHub(null, []);

        // Assert
        result.ShouldBeOfType<ProviderChecks.NoneDetected>();
    }

    [Theory]
    [InlineData("SUCCESS")]
    public void WhenRollupStateIsSuccess_ReturnsGreen(string state)
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitHub(state, []);

        // Assert
        result.ShouldBeOfType<ProviderChecks.Green>();
    }

    [Theory]
    [InlineData("FAILURE")]
    [InlineData("ERROR")]
    public void WhenRollupStateIsFailureOrError_ReturnsRedWithFailingChecks(string state)
    {
        // Arrange
        FailingCheck failing = new("lint", "FAILURE", "https://ci.example.com/1");

        // Act
        ProviderChecks result = ProviderChecks.FromGitHub(state, [failing]);

        // Assert
        ProviderChecks.Red red = result.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldContain(failing);
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("EXPECTED")]
    public void WhenRollupStateIsPendingOrExpected_ReturnsPending(string state)
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitHub(state, []);

        // Assert
        result.ShouldBeOfType<ProviderChecks.Pending>();
    }

    [Theory]
    [InlineData("STALE")]
    [InlineData("ACTION_REQUIRED")]
    [InlineData("unknown_future_value")]
    public void WhenRollupStateIsUnrecognized_ReturnsPendingConservatively(string state)
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitHub(state, []);

        // Assert
        // Unknown states map to Pending so the gate waits rather than falsely resolving.
        result.ShouldBeOfType<ProviderChecks.Pending>();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("pending")]
    public void WhenRollupStateIsLowercase_MapsCorrectly(string state)
    {
        // Arrange
        // Act — factory must be case-insensitive per spec
        ProviderChecks result = ProviderChecks.FromGitHub(state, []);

        // Assert
        bool isExpectedType = state.ToUpperInvariant() switch
        {
            "SUCCESS" => result is ProviderChecks.Green,
            "FAILURE" => result is ProviderChecks.Red,
            "PENDING" => result is ProviderChecks.Pending,
            _ => false,
        };
        isExpectedType.ShouldBeTrue($"state '{state}' should map case-insensitively");
    }
}
