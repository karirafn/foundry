namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>What one Publisher proxy-probe found out about the Package Source Proxy.</summary>
internal abstract record PackageProxyProbeOutcome
{
    private PackageProxyProbeOutcome()
    {
    }

    /// <summary>The proxy answered with an HTTP status line, whatever its status: a worker can reach it again.</summary>
    public sealed record Reachable : PackageProxyProbeOutcome;

    /// <summary>No HTTP response came back: DNS failure, refused or unreachable connection, or timeout.</summary>
    public sealed record Unreachable(string Detail) : PackageProxyProbeOutcome;

    /// <summary>The probe reported nothing: the container could not run, timed out, or printed no result line.</summary>
    public sealed record Inconclusive(string Reason) : PackageProxyProbeOutcome;
}
