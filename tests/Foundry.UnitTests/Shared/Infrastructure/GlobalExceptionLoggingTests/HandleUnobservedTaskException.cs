using System.Runtime.ExceptionServices;

using Foundry.ServiceDefaults;
using Foundry.Testing;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.GlobalExceptionLoggingTests;

public sealed class HandleUnobservedTaskException
{
    // The Polly frame is the one the CI api job failure of 2026-11-18 (main a6f7c30d, PR #1132) reported.
    private const string PollyExecutorStackTrace =
        "   at System.Threading.SemaphoreSlim.CheckDispose()\n"
        + "   at System.Threading.SemaphoreSlim.WaitAsync(Int32 millisecondsTimeout, CancellationToken cancellationToken)\n"
        + "   at Polly.CircuitBreaker.ScheduledTaskExecutor.StartProcessingAsync()";

    private const string OtherStackTrace =
        "   at System.Threading.SemaphoreSlim.CheckDispose()\n"
        + "   at System.Threading.SemaphoreSlim.WaitAsync(Int32 millisecondsTimeout, CancellationToken cancellationToken)\n"
        + "   at Foundry.Modules.Workers.Infrastructure.SomeLoop.RunAsync()";

    [Fact]
    public void WhenEveryInnerExceptionIsThePollyExecutorDisposalRace_LogsWarningAndMarksObserved()
    {
        // Arrange
        CapturingLogger logger = new();
        AggregateException aggregateException = new(
            SemaphoreDisposedAt(PollyExecutorStackTrace),
            SemaphoreDisposedAt(PollyExecutorStackTrace));
        UnobservedTaskExceptionEventArgs args = new(aggregateException);

        // Act
        GlobalExceptionLogging.HandleUnobservedTaskException(logger, args);

        // Assert
        logger.Entries.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            entry => entry.Level.ShouldBe(LogLevel.Warning),
            entry => entry.Message.ShouldContain("Polly"),
            entry => entry.Exception.ShouldBe(aggregateException));
        args.Observed.ShouldBeTrue("SetObserved() must be called so the finalizer does not re-throw");
    }

    [Fact]
    public void WhenObjectDisposedExceptionComesFromAnotherSource_LogsCritical()
    {
        // Arrange
        CapturingLogger logger = new();
        AggregateException aggregateException = new(SemaphoreDisposedAt(OtherStackTrace));
        UnobservedTaskExceptionEventArgs args = new(aggregateException);

        // Act
        GlobalExceptionLogging.HandleUnobservedTaskException(logger, args);

        // Assert
        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Critical);
        args.Observed.ShouldBeTrue();
    }

    [Fact]
    public void WhenThePollyExecutorDisposalRaceIsMixedWithAnotherException_LogsCritical()
    {
        // Arrange
        CapturingLogger logger = new();
        AggregateException aggregateException = new(
            SemaphoreDisposedAt(PollyExecutorStackTrace),
            new InvalidOperationException("task failure"));
        UnobservedTaskExceptionEventArgs args = new(aggregateException);

        // Act
        GlobalExceptionLogging.HandleUnobservedTaskException(logger, args);

        // Assert
        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Critical);
        args.Observed.ShouldBeTrue();
    }

    private static ObjectDisposedException SemaphoreDisposedAt(string stackTrace)
    {
        ObjectDisposedException exception = new("System.Threading.SemaphoreSlim");
        ExceptionDispatchInfo.SetRemoteStackTrace(exception, stackTrace);
        return exception;
    }
}
