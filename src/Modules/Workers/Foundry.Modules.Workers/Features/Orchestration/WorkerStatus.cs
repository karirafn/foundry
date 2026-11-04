namespace Foundry.Modules.Workers.Features.Orchestration;

/// <summary>
/// Snapshot of a worker container's state. <see cref="OomKilled"/> mirrors Docker's
/// <c>State.OOMKilled</c> flag: the kernel killed the container for exceeding its memory limit.
/// </summary>
internal sealed record WorkerStatus(
    bool IsRunning,
    int? ExitCode,
    DateTimeOffset? FinishedAt,
    bool OomKilled = false);
