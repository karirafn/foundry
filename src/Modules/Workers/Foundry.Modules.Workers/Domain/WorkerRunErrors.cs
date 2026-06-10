using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain;

internal static class WorkerRunErrors
{
    internal const string NotFoundCode = "WorkerRun.NotFound";

    internal static Error NotFound(WorkerRunId id) =>
        new(NotFoundCode, $"Worker run '{id.Value}' was not found.");
}
