using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain;

internal static class WorkerRunErrors
{
    internal const string NotFoundCode = "WorkerRun.NotFound";

    public static Error NotFound(WorkerRunId id) =>
        new(NotFoundCode, $"Worker run '{id.Value}' was not found.");

    internal const string NotActiveCode = "WorkerRun.NotActive";

    public static Error NotActive(WorkerRunId id) =>
        new(NotActiveCode, $"Worker run '{id.Value}' is not active and cannot accept reports.");
}
