// IDE0065: alias placed inside namespace to avoid CS0118 — 'Checks' is also the enclosing namespace segment.
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Checks;

#pragma warning disable IDE0065 // Misplaced using directive
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
#pragma warning restore IDE0065

public sealed class FromGitLab
{
    [Fact]
    public void WhenPipelineStatusIsNull_ReturnsNoneDetected()
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitLab(null, []);

        // Assert
        result.ShouldBeOfType<ProviderChecks.NoneDetected>();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("skipped")]
    public void WhenPipelineStatusIsSuccessOrSkipped_ReturnsGreen(string status)
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitLab(status, []);

        // Assert
        result.ShouldBeOfType<ProviderChecks.Green>();
    }

    [Fact]
    public void WhenPipelineStatusIsFailed_ReturnsRedWithFailingChecks()
    {
        // Arrange
        FailingCheck failing = new("lint", "failed", "https://gitlab.example.com/jobs/1");

        // Act
        ProviderChecks result = ProviderChecks.FromGitLab("failed", [failing]);

        // Assert
        ProviderChecks.Red red = result.ShouldBeOfType<ProviderChecks.Red>();
        red.FailingChecks.ShouldContain(failing);
    }

    [Theory]
    [InlineData("created")]
    [InlineData("waiting_for_resource")]
    [InlineData("preparing")]
    [InlineData("pending")]
    [InlineData("running")]
    [InlineData("scheduled")]
    [InlineData("waiting_for_callback")]
    public void WhenPipelineStatusIsActiveOrQueued_ReturnsPending(string status)
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitLab(status, []);

        // Assert
        result.ShouldBeOfType<ProviderChecks.Pending>();
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("canceling")]
    [InlineData("manual")]
    public void WhenPipelineStatusIsCanceledOrManual_ReturnsUnfinished(string status)
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitLab(status, []);

        // Assert
        result.ShouldBeOfType<ProviderChecks.Unfinished>();
    }

    [Theory]
    [InlineData("unknown_status")]
    [InlineData("future_gitlab_status")]
    [InlineData("RUNNING")]
    public void WhenPipelineStatusIsUnrecognized_ReturnsPendingConservatively(string status)
    {
        // Arrange
        // Act
        ProviderChecks result = ProviderChecks.FromGitLab(status, []);

        // Assert
        // Unknown statuses map to Pending so the gate waits rather than falsely resolving.
        result.ShouldBeOfType<ProviderChecks.Pending>();
    }
}
