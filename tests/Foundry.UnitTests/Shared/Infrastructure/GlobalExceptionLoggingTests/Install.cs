using Foundry.ServiceDefaults;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Infrastructure.GlobalExceptionLoggingTests;

public sealed class Install
{
    [Fact]
    public void WhenUnhandledExceptionOccurs_LogsCritical()
    {
        // Arrange
        CapturingLogger logger = new();
        InvalidOperationException exception = new("boom");

        // Act
        GlobalExceptionLogging.HandleUnhandledException(logger, exception, isTerminating: false);

        // Assert
        logger.Entries.ShouldContain(
            e => e.Level == LogLevel.Critical && e.Exception == exception,
            "unhandled exception must be logged at Critical level");
    }

    [Fact]
    public void WhenUnobservedTaskExceptionOccurs_LogsCriticalAndMarksObserved()
    {
        // Arrange
        CapturingLogger logger = new();
        InvalidOperationException innerException = new("task failure");
        AggregateException aggregateException = new(innerException);
        UnobservedTaskExceptionEventArgs args = new(aggregateException);

        // Act
        GlobalExceptionLogging.HandleUnobservedTaskException(logger, args);

        // Assert
        logger.Entries.ShouldContain(
            e => e.Level == LogLevel.Critical && e.Exception == aggregateException,
            "unobserved task exception must be logged at Critical level");
        args.Observed.ShouldBeTrue("SetObserved() must be called so the finalizer does not re-throw");
    }
}
