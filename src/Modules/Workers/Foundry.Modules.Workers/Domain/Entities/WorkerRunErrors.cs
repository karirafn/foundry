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

    internal const string NotAwaitingPublishRetryCode = "WorkerRun.NotAwaitingPublishRetry";

    internal static Error NotAwaitingPublishRetry(WorkerRunId id) =>
        new(NotAwaitingPublishRetryCode, $"Worker run '{id.Value}' is not waiting for a publish retry.");

    internal const string NotHoldingUnpublishedWorkCode = "WorkerRun.NotHoldingUnpublishedWork";

    internal static Error NotHoldingUnpublishedWork(WorkerRunId id) =>
        new(NotHoldingUnpublishedWorkCode, $"Worker run '{id.Value}' is not holding unpublished work that waits for an operator.");

    internal const string NotPublishBlockedCode = "WorkerRun.NotPublishBlocked";

    internal static Error NotPublishBlocked(WorkerRunId id) =>
        new(NotPublishBlockedCode, $"Worker run '{id.Value}' is not blocked by secret findings.");

    internal const string FindingsChangedCode = "WorkerRun.FindingsChanged";

    internal static Error FindingsChanged(WorkerRunId id) =>
        new(FindingsChangedCode, $"Worker run '{id.Value}' was blocked again on findings the confirmation did not see.");

    internal const string PublishBlockedAtRequiredCode = "WorkerRun.PublishBlockedAtRequired";

    internal static Error PublishBlockedAtRequired() =>
        new(PublishBlockedAtRequiredCode, "The confirmation must echo the run's publishBlockedAt.");
}
