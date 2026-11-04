using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain.Entities;

internal static class WorkerRunErrors
{
    internal const string NotFoundCode = "WorkerRun.NotFound";

    internal static Error NotFound(WorkerRunId id) =>
        new(NotFoundCode, $"Worker run '{id.Value}' was not found.");

    internal const string NotCancellableCode = "WorkerRun.NotCancellable";

    internal static Error NotCancellable(WorkerRunId id) =>
        new(NotCancellableCode, $"Worker run '{id.Value}' is not running and cannot be cancelled.");
}
