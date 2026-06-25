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
    /// Logs the unobserved task exception at Critical level and marks it observed so the
    /// finalizer does not re-throw. Exposed for direct testing.
    /// </summary>
    public static void HandleUnobservedTaskException(ILogger logger, UnobservedTaskExceptionEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(e);

        logger.LogCritical(e.Exception, "Unobserved task exception.");

        e.SetObserved();
    }
}
