namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The <c>nuget.config</c> a run's worker restores with (ADR 0112): the repository's root config with its Package
/// Sources re-pointed at the Package Source Proxy and the run's Run Source Token as their credential. It carries the
/// token, so it is handed to the worker only and never logged or stored.
/// </summary>
public sealed record WorkerNuGetConfig(string Xml)
{
    public override string ToString() => $"{nameof(WorkerNuGetConfig)} {{ Xml = *** }}";
}
