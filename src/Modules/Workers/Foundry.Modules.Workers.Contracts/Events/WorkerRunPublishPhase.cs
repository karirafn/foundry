namespace Foundry.Modules.Workers.Contracts;

/// <summary>Where a run that stopped working stands in publishing its commits (ADR 0100).</summary>
public enum WorkerRunPublishPhase
{
    /// <summary>The Publisher is pushing, or about to push, the run's commits.</summary>
    Publishing,

    /// <summary>A publish attempt failed transiently; Foundry retries it with backoff.</summary>
    Retrying,
}
