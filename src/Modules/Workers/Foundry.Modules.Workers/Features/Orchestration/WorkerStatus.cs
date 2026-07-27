namespace Foundry.Modules.Workers.Features.Orchestration;

internal sealed record WorkerStatus(
    bool IsRunning,
    int? ExitCode,
    DateTimeOffset? FinishedAt);
