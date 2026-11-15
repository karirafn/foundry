// IDE0065: alias placed inside namespace to avoid CS0118 — 'Checks' is also the enclosing namespace segment.
using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.Checks;

#pragma warning disable IDE0065 // Misplaced using directive
using ProviderChecks = Foundry.Modules.Monitoring.Features.Providers.Checks.Checks;
#pragma warning restore IDE0065

// The reason tokens are values of a GitLab job's failure_reason, defined by GitLab's CommitStatus failure_reasons
// enum (app/models/concerns/enums/ci/commit_status.rb); ci_quota_exceeded is the reason observed on the pipeline
// in issue #968.
public sealed class IsGitLabInfrastructureFailure
{
    [Theory]
    [InlineData("script_failure")]
    [InlineData("job_execution_timeout")]
    [InlineData("job_execution_server_timeout")]
    [InlineData("server_timeout_running")]
    public void WhenTheReasonSaysTheScriptRan_ReturnsFalse(string failureReason)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitLabInfrastructureFailure(failureReason);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ci_quota_exceeded")]
    [InlineData("runner_system_failure")]
    [InlineData("unknown_failure")]
    [InlineData("stuck_or_timeout_failure")]
    [InlineData("a_reason_gitlab_adds_later")]
    public void WhenTheReasonIsNotOneWhereTheScriptRan_ReturnsTrue(string failureReason)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitLabInfrastructureFailure(failureReason);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WhenTheJobCarriesNoReason_ReturnsFalse(string? failureReason)
    {
        // Arrange
        // Act
        bool result = ProviderChecks.IsGitLabInfrastructureFailure(failureReason);

        // Assert
        result.ShouldBeFalse();
    }
}
