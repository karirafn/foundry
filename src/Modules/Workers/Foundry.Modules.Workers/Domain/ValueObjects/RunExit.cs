namespace Foundry.Modules.Workers.Domain.ValueObjects;

/// <summary>Why a run's container stopped: on its own, or because Foundry stopped it.</summary>
public enum RunStopReason
{
    /// <summary>The container exited on its own (or was removed outside Foundry).</summary>
    Exited,

    /// <summary>Foundry stopped the container at the run timeout.</summary>
    TimedOut,

    /// <summary>Foundry stopped the container because a user cancelled the run.</summary>
    Cancelled,
}

/// <summary>
/// How a run's container ended, kept while its commits are published (ADR 0100) so the outcome can be resolved once
/// publishing finishes, after the container itself is gone.
/// </summary>
/// <param name="ExitCode">The container's exit code, or <see langword="null"/> when the container was already gone.</param>
/// <param name="OomKilled">Whether the kernel killed the container for running out of memory.</param>
/// <param name="StopReason">Whether Foundry stopped the container, and why.</param>
/// <param name="ExitedAt">When Foundry observed the end of the container.</param>
public sealed record RunExit(int? ExitCode, bool OomKilled, RunStopReason StopReason, DateTimeOffset ExitedAt)
{
    /// <summary>The container finished its turn (exit 0) on its own, so its verdict counts.</summary>
    public bool FinishedCleanly => StopReason == RunStopReason.Exited && ExitCode == 0;
}
