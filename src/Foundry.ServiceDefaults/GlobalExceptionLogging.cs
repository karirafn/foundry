using Microsoft.Extensions.Logging;

namespace Foundry.ServiceDefaults;

/// <summary>
/// Installs process-wide last-resort handlers so unhandled exceptions and unobserved task exceptions
/// are logged at Critical level rather than terminating the host silently with exit code -1.
/// </summary>
public static class GlobalExceptionLogging
{
    /// <summary>
    /// Subscribes both <see cref="AppDomain.UnhandledException"/> and
    /// <see cref="TaskScheduler.UnobservedTaskException"/> to log at Critical using the
    /// supplied logger factory. Call this after <c>builder.Build()</c> so a real logger
    /// is available, and before <c>app.Run()</c>.
    /// </summary>
    public static void Install(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ILogger logger = loggerFactory.CreateLogger("Foundry.GlobalExceptionLogging");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HandleUnhandledException(logger, e.ExceptionObject, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
            HandleUnobservedTaskException(logger, e);
    }

    /// <summary>
    /// Logs the exception object at Critical level. Exposed for direct testing without
    /// raising a real process-fatal event.
    /// </summary>
    public static void HandleUnhandledException(ILogger logger, object exceptionObject, bool isTerminating)
    {
        ArgumentNullException.ThrowIfNull(logger);

        Exception? exception = exceptionObject as Exception;

        logger.LogCritical(
            exception,
            "Unhandled exception{Terminating}.",
            isTerminating ? " (process is terminating)" : string.Empty);
    }

    /// <summary>
    /// Logs the unobserved task exception at Critical level (Warning for the known Polly executor
    /// disposal race) and marks it observed so the finalizer does not re-throw. Exposed for
    /// direct testing.
    /// </summary>
    public static void HandleUnobservedTaskException(ILogger logger, UnobservedTaskExceptionEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(e);

        if (IsPollyExecutorDisposalRace(e.Exception))
        {
            logger.LogWarning(
                e.Exception,
                "Unobserved task exception from the known Polly circuit-breaker executor disposal race; the disposed executor had no work left.");
        }
        else
        {
            logger.LogCritical(e.Exception, "Unobserved task exception.");
        }

        e.SetObserved();
    }

    // Polly's ScheduledTaskExecutor starts its loop with Task.Run and disposes its semaphore on
    // Dispose; a pipeline disposed before the loop reaches WaitAsync faults a task nobody observes
    // (still present in Polly.Core 8.9.1). The executor type is internal, so it can only be matched
    // by its stack frame, and a disposed executor has no work left to lose. Every inner exception
    // must match, so a real failure aggregated alongside it still logs at Critical.
    private static bool IsPollyExecutorDisposalRace(AggregateException exception) =>
        exception.InnerExceptions.Count > 0
        && exception.InnerExceptions.All(inner =>
            inner is ObjectDisposedException
            && inner.StackTrace?.Contains(
                "Polly.CircuitBreaker.ScheduledTaskExecutor.StartProcessingAsync",
                StringComparison.Ordinal) == true);
}
