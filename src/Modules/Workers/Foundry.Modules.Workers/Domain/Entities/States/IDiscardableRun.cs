namespace Foundry.Modules.Workers.Domain.Entities.States;

/// <summary>
/// An unpublished run that waits for an operator, keeping its Hand-off volume until it is discarded (ADR 0100, #788):
/// a run whose publish retries ran out, or whose publish the Diff gate blocked.
/// </summary>
public interface IDiscardableRun
{
    /// <summary>The Hand-off volume reached its time to live: the unpublished work is discarded and the run fails.</summary>
    FailedRun Discard();

    /// <summary>An operator discarded the run: its unpublished work is deleted and nothing is pushed.</summary>
    FailedRun DiscardOnRequest();
}
