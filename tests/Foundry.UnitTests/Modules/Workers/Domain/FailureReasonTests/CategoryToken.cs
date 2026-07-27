using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.FailureReasonTests;

public sealed class CategoryToken
{
    [Fact]
    public void WhenNonZeroExit_CategoryTokenIsNonZeroExit()
    {
        // Arrange
        FailureReason reason = new FailureReason.NonZeroExit(ExitCode: 1);

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("non_zero_exit");
    }

    [Fact]
    public void WhenTimedOut_CategoryTokenIsTimedOut()
    {
        // Arrange
        FailureReason reason = new FailureReason.TimedOut();

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("timed_out");
    }

    [Fact]
    public void WhenContainerError_CategoryTokenIsContainerError()
    {
        // Arrange
        FailureReason reason = new FailureReason.ContainerError(Message: "oom killed");

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("container_error");
    }

    [Fact]
    public void WhenUsageLimited_CategoryTokenIsUsageLimited()
    {
        // Arrange
        FailureReason reason = new FailureReason.UsageLimited(
            ResetsAt: new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("usage_limited");
    }

    [Fact]
    public void WhenWorkerBootstrapFailed_CategoryTokenIsWorkerBootstrapFailed()
    {
        // Arrange
        FailureReason reason = new FailureReason.WorkerBootstrapFailed(Detail: "git clone failed");

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("worker_bootstrap_failed");
    }

    [Fact]
    public void WhenProviderError_CategoryTokenIsProviderError()
    {
        // Arrange
        FailureReason reason = new FailureReason.ProviderError(Message: "Branch pre-creation on acme/repo returned 403");

        // Act
        string token = reason.CategoryToken;

        // Assert
        token.ShouldBe("provider_error");
    }
}
