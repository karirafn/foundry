namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The log of the CI job behind a failing check, read from the provider for a failed-checks revision prompt.
/// </summary>
public abstract record CiJobLog
{
    private CiJobLog()
    {
    }

    /// <summary>
    /// The end of the job's log as the provider returned it, and the lines near its start that look like failures.
    /// </summary>
    /// <param name="Text">The log's final bytes, decoded.</param>
    /// <param name="Length">The whole log's length in bytes; <paramref name="Text"/> ends at this offset.</param>
    /// <param name="FailureExcerpt">
    /// Lines that look like failures, each with the lines that follow it, read from the start of the log in log
    /// order. A line may also fall inside <paramref name="Text"/>.
    /// </param>
    public sealed record Available(string Text, long Length, IReadOnlyList<JobLogLine> FailureExcerpt) : CiJobLog;

    /// <summary>
    /// The check is not a CI job the provider exposes logs for (a commit status, a third-party check app),
    /// so it is listed without a log.
    /// </summary>
    public sealed record NotACiJob : CiJobLog;
}
