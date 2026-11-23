namespace Foundry.Modules.Workers.Contracts;

/// <summary>What an issue's run is doing outside its worker container (ADR 0100).</summary>
public enum WorkerRunPhase
{
    /// <summary>The Publisher prepares the run (fetch, branch pre-creation) before its container starts.</summary>
    Starting,

    /// <summary>The Publisher is pushing, or about to push, the run's commits.</summary>
    Publishing,

    /// <summary>A publish attempt failed transiently; Foundry retries it with backoff.</summary>
    Retrying,
}
