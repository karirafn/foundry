---
status: accepted
supersedes: 0008-aspire-image-build-with-docker-orchestrator.md
---

# Worker Image Build as Hosted Lifecycle Service

## Context

The MSBuild target approach (ADR 0008) only fires during Aspire orchestration.
Direct WebApi launches skip the build entirely, leaving a stale or absent `foundry-worker:local` image.
This caused a permissions failure: when mount-point directories do not exist inside the image, Docker daemon creates them at runtime as root, making them inaccessible to the non-root `node` user.

## Decision

Move image building to `WorkerImageBuildService : IHostedLifecycleService` in the Workers module.
The service runs in `StartingAsync`, blocking until the build completes before Kestrel binds and accepts requests.
Building is controlled by `Workers:ImageBuild:Enabled` (default `true`); set to `false` to skip when a pre-built image is already available.
The implementation calls `DockerClient.Images.BuildImageFromDockerfileAsync` with a tar context assembled from the `workers/` directory via `System.Formats.Tar.TarFile.CreateFromDirectory`.

## Considered Options

- **MSBuild target in AppHost** — skipped on direct WebApi launch; removed.
- **On-demand build at first dispatch** — races with concurrent dispatches; adds latency to the first worker.

## Consequences

The image is guaranteed to exist before any worker dispatch can occur, regardless of how the WebApi is started.
The AppHost `AddDockerfile` call and its no-op CMD container are no longer needed and have been removed.
