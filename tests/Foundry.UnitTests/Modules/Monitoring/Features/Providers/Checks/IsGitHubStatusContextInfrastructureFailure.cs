// IDE0065: alias placed inside namespace to avoid CS0118 — 'Checks' is also the enclosing namespace segment.
using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Checks;

#pragma warning disable IDE0065 // Misplaced using directive
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
#pragma warning restore IDE0065

// The state tokens are the StatusState values of GitHub's GraphQL API:
// https://docs.github.com/en/graphql/reference/commits (StatusState, StatusContext).
public sealed class IsGitHubStatusContextInfrastructureFailure
{
    [Theory]
    [InlineData("ERROR")]
    [InlineData("error")]
    public void WhenTheContextErrored_ReturnsTrue(string state)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitHubStatusContextInfrastructureFailure(state);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("FAILURE")]
    [InlineData("failure")]
    [InlineData(null)]
    public void WhenTheContextFailedOrTheStateIsUnknown_ReturnsFalse(string? state)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitHubStatusContextInfrastructureFailure(state);

        // Assert
        result.ShouldBeFalse();
    }
}
