namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The log of the CI job behind a failing check, read from the provider for a failed-checks revision prompt.
/// </summary>
public abstract record CiJobLog
{
    private CiJobLog()
    {
    }

    /// <summary>The job's full log text as the provider returned it.</summary>
    public sealed record Available(string Text) : CiJobLog;

    /// <summary>
    /// The check is not a CI job the provider exposes logs for (a commit status, a third-party check app),
    /// so it is listed without a log.
    /// </summary>
    public sealed record NotACiJob : CiJobLog;
}
