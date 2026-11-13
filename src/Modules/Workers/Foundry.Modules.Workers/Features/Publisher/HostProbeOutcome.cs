namespace Foundry.Modules.Workers.Features.Publisher;

/// <summary>What one Publisher probe of a repository found out about its provider host.</summary>
internal abstract record HostProbeOutcome
{
    private HostProbeOutcome()
    {
    }

    /// <summary>The provider answered, whatever it answered: a dispatch to the host can reach it again.</summary>
    public sealed record Reachable : HostProbeOutcome;

    /// <summary>The provider could not be reached, exactly as Prepare's <c>provider_unreachable</c> means it.</summary>
    public sealed record Unreachable(string Detail) : HostProbeOutcome;

    /// <summary>The probe reported nothing: the container could not run, timed out, or printed no result line.</summary>
    public sealed record Inconclusive(string Reason) : HostProbeOutcome;
}
