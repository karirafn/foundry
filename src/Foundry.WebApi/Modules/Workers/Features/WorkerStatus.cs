namespace Foundry.WebApi.Modules.Workers.Features;

public sealed record WorkerStatus(
    bool IsRunning,
    int? ExitCode,
    DateTimeOffset? FinishedAt);
