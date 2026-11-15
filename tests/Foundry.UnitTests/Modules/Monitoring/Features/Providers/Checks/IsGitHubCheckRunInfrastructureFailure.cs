// IDE0065: alias placed inside namespace to avoid CS0118 — 'Checks' is also the enclosing namespace segment.
using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Checks;

#pragma warning disable IDE0065 // Misplaced using directive
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
#pragma warning restore IDE0065

// The conclusion tokens are the CheckConclusionState values of GitHub's GraphQL API:
// https://docs.github.com/en/graphql/reference/checks (CheckConclusionState, CheckRun).
public sealed class IsGitHubCheckRunInfrastructureFailure
{
    [Theory]
    [InlineData("STARTUP_FAILURE")]
    [InlineData("CANCELLED")]
    [InlineData("ACTION_REQUIRED")]
    [InlineData("startup_failure")]
    public void WhenTheRunFailedBeforeItsScriptRan_ReturnsTrue(string conclusion)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitHubCheckRunInfrastructureFailure(conclusion);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("FAILURE")]
    [InlineData("TIMED_OUT")]
    [InlineData("failure")]
    [InlineData(null)]
    public void WhenTheRunsScriptFailedOrTheConclusionIsUnknown_ReturnsFalse(string? conclusion)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitHubCheckRunInfrastructureFailure(conclusion);

        // Assert
        result.ShouldBeFalse();
    }
}
