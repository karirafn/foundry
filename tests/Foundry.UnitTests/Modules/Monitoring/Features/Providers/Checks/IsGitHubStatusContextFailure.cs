// IDE0065: alias placed inside namespace to avoid CS0118 — 'Checks' is also the enclosing namespace segment.
using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Checks;

#pragma warning disable IDE0065 // Misplaced using directive
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
#pragma warning restore IDE0065

// The state tokens are the StatusState values of GitHub's GraphQL API:
// https://docs.github.com/en/graphql/reference/commits (StatusState, StatusContext).
public sealed class IsGitHubStatusContextFailure
{
    [Theory]
    [InlineData("FAILURE")]
    [InlineData("ERROR")]
    public void WhenStateIsAFailingValue_ReturnsTrue(string state)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitHubStatusContextFailure(state);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("SUCCESS")]
    [InlineData("PENDING")]
    [InlineData("EXPECTED")]
    [InlineData(null)]
    public void WhenStateIsNotFailing_ReturnsFalse(string? state)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitHubStatusContextFailure(state);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("error")]
    public void WhenStateIsLowercase_NormalisesCaseLikeFromGitHub(string state)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitHubStatusContextFailure(state);

        // Assert
        result.ShouldBeTrue();
    }
}
