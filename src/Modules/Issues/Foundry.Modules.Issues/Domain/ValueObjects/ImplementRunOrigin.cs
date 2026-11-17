namespace Foundry.Modules.Issues.Domain.ValueObjects;

/// <summary>
/// Where a running implement run came from (#1060): a fresh run from the queue, or a continuation of a failed run's
/// branch. It decides the run's <see cref="RunKind"/> while it is in flight.
/// </summary>
public enum ImplementRunOrigin
{
    Fresh,
    Continuation,
}
