namespace Foundry.Modules.Workers.Features;

internal sealed record WorkerStatus(
    bool IsRunning,
    int? ExitCode,
    DateTimeOffset? FinishedAt);
