namespace Foundry.Modules.Workers.Features;

internal abstract record WorkerStatusProbe
{
    private WorkerStatusProbe()
    {
    }

    /// <summary>The daemon answered and the container exists (running or exited).</summary>
    internal sealed record Available(WorkerStatus Status) : WorkerStatusProbe;

    /// <summary>The daemon answered and the container is definitively gone.</summary>
    internal sealed record NotFound : WorkerStatusProbe;

    /// <summary>The daemon could not be reached (connectivity failure) — transient, retried next tick.</summary>
    internal sealed record Unreachable : WorkerStatusProbe;
}
