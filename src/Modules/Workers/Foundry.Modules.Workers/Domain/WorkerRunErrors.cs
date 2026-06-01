using Foundry.Shared;

namespace Foundry.Modules.Workers.Domain;

internal static class WorkerRunErrors
{
    internal const string NotFoundCode = "WorkerRun.NotFound";

    internal static Error NotFound(WorkerRunId id) =>
        new(NotFoundCode, $"Worker run '{id.Value}' was not found.");

    internal const string NotActiveCode = "WorkerRun.NotActive";

    internal static Error NotActive(WorkerRunId id) =>
        new(NotActiveCode, $"Worker run '{id.Value}' is not active and cannot accept reports.");

    internal const string ContentTooLargeCode = "WorkerRun.ContentTooLarge";

    internal static Error ContentTooLarge(int maxLength) =>
        new(ContentTooLargeCode, $"Report content exceeds the maximum allowed length of {maxLength} characters.");
}
