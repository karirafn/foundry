# Worker Image Rebuild Queue: Hosted Channel Consumer

## Context

When the user changes `WorkerImageConfiguration` flags (e.g. enable .NET tooling), the worker Docker image must be rebuilt. The rebuild is a long-running operation (minutes) that must survive transient failures, update `GlobalSettings.ImageBuildStatus` in the database, and broadcast progress notifications to the UI. It can only run one rebuild at a time.

## Decision

Use a bounded `Channel<bool>` (capacity 1, `DropWrite` overflow) as the rebuild queue, consumed by a dedicated `BackgroundService` (`WorkerImageRebuildService`). The service reads signals from `IWorkerImageRebuildQueue.ReadAllAsync()` and, for each signal:

1. Broadcasts a "building" `SystemNotification` immediately so the UI can show a banner.
2. Resolves a scoped `DbContext` via `IServiceScopeFactory` (background service pattern — never capture scoped services in the singleton consumer).
3. Loads `GlobalSettings`, calls `BeginImageBuild()`, and persists.
4. Builds the image via `IImageOperations` using `ToBuildArgs()` from the persisted `WorkerImageConfiguration`.
5. On success: calls `CompleteImageBuild()`, persists, and broadcasts an inactive notification (clears the banner).
6. On failure: calls `FailImageBuild(errorTail)`, persists, and broadcasts an active notification containing the error tail.

The channel's `DropWrite` mode ensures that multiple rapid configuration saves collapse into a single rebuild — there is no value in queuing more than one pending signal.

## Considered Options

- **Direct async call from the update handler** — would block the HTTP response for the duration of the build and loses status-transition ownership to the handler layer.
- **`IHostedLifecycleService.StartingAsync`** — the existing `WorkerImageBuildService` uses this for startup builds from config. On-demand rebuilds need to be triggered at runtime, not only on startup, so a channel consumer is the correct pattern.
- **Outbox / scheduled job** — over-engineered for a single-instance, single-consumer workload.

## Consequences

- Status transitions (`BeginImageBuild`, `CompleteImageBuild`, `FailImageBuild`) are owned exclusively by `WorkerImageRebuildService`, not by the endpoint handler. The handler only persists the configuration change and enqueues a signal.
- The channel's `DropWrite` policy is visible: `TryEnqueue()` returns `false` when a rebuild is already pending, allowing callers to log or surface the drop.
- A new constant `ImageBuildCategory = "image-build"` is introduced for `SystemNotification` routing on the frontend.
