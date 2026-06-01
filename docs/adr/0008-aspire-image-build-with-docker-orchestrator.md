# Aspire Image Build with Docker Orchestrator Runtime Dispatch

## Context

Foundry dispatches ephemeral Docker containers running Claude Code to implement issues. The worker image needs to be built from a Dockerfile, and containers need to be created on-demand per issue. Aspire's `AddDockerfile` builds and starts a container, but workers are not long-running services — they are created per-issue by `DockerWorkerOrchestrator`.

## Decision

Use `AddDockerfile` in AppHost purely as an image builder. The Dockerfile's default `CMD` is a no-op (`echo "foundry-worker image ready"`) so the Aspire-managed container exits immediately after build. `DockerWorkerOrchestrator` creates runtime containers from the built `foundry-worker:local` image, passing `["/entrypoint.sh"]` as the command.

This separates the image build concern (Aspire, runs once and caches) from the runtime dispatch concern (`DockerWorkerOrchestrator`, creates containers per issue).
