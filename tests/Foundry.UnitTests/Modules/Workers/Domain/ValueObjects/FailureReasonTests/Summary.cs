using System;

using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

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

    [Fact]
    public void WhenTransientApiError_SummaryDescribesTransientFault()
    {
        // Arrange
        FailureReason reason = new FailureReason.TransientApiError();

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Transient Anthropic API fault");
    }

    [Fact]
    public void WhenCreditsExhausted_SummaryIsCreditsExhausted()
    {
        // Arrange
        FailureReason reason = new FailureReason.CreditsExhausted();

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldBe("Credits exhausted");
    }

    [Fact]
    public void WhenCreditsExhausted_SummaryDoesNotStartWithUsageLimitReached()
    {
        // Arrange
        // Guard: DispatchResumedHandler re-queues via EF.Functions.Like(FailureReason, "Usage limit reached%").
        // A shared prefix would make usage-limit resume wrongly sweep credit-blocked issues.
        FailureReason reason = new FailureReason.CreditsExhausted();

        // Act
        string summary = reason.Summary;

        // Assert
        summary.ShouldNotStartWith("Usage limit reached");
    }
}
