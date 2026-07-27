using System;

using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.FailureReasonTests;

public sealed class Summary
{
    [Fact]
    public void WhenNonZeroExit_SummaryContainsExitCode()
    {
        // Arrange
        FailureReason reason = new FailureReason.NonZeroExit(ExitCode: 42);

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Non-zero exit code: 42");
    }

    [Fact]
    public void WhenTimedOut_SummaryDescribesTimeout()
    {
        // Arrange
        FailureReason reason = new FailureReason.TimedOut();

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Worker run timed out");
    }

    [Fact]
    public void WhenContainerError_SummaryContainsMessage()
    {
        // Arrange
        FailureReason reason = new FailureReason.ContainerError(Message: "OOM killed");

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Container error: OOM killed");
    }

    [Fact]
    public void WhenUsageLimited_SummaryIsUsageLimitedText()
    {
        // Arrange
        FailureReason reason = new FailureReason.UsageLimited(
            ResetsAt: new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Usage limit reached");
    }

    [Fact]
    public void WhenWorkerBootstrapFailed_SummaryContainsDetail()
    {
        // Arrange
        FailureReason reason = new FailureReason.WorkerBootstrapFailed(Detail: "rootless dockerd");

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Worker bootstrap failed: rootless dockerd");
    }

    [Fact]
    public void WhenProviderError_SummaryReturnsMessageVerbatim()
    {
        // Arrange
        FailureReason reason = new FailureReason.ProviderError(
            Message: "Branch pre-creation on acme/repo returned 403 — token lacks contents=write");

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Branch pre-creation on acme/repo returned 403 — token lacks contents=write");
    }
}
