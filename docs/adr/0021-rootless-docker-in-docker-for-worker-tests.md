---
status: accepted
---

# Rootless Docker-in-Docker for Worker Integration Tests

> Updated by #221: daemon startup is now best-effort — failure is non-fatal and surfaces the `FOUNDRY_DOCKER_UNAVAILABLE` sentinel; the worker continues in degraded mode.

## Context

Workers run arbitrary repository code, including Testcontainers-based integration tests that need a Docker daemon (e.g. `Testcontainers.MsSql`).
Integration-first TDD requires the red-green-refactor loop to run inside the worker, so the daemon must be reachable locally rather than deferred to CI.
A worker is itself a container running untrusted, model-driven code, so however it reaches a daemon becomes a host-escape surface that must be bounded.

The worker image already gained a `INSTALL_DOCKER` build-arg ([ADR 0019](0019-worker-image-flags-in-settings.md) flag plumbing) that installs the Docker engine binaries, but nothing started a daemon and no run-spec allowances existed for one to run nested.

## Decision

Workers that need Docker run their **own rootless `dockerd`** started by the entrypoint, and Testcontainers is pointed at that nested socket via `DOCKER_HOST=unix:///home/node/.runtime/docker.sock`.
The host Docker daemon is never mounted or reachable, so a worker escape lands as the unprivileged `node` user (UID 1000) mapped into the host's subordinate-ID range (`node:100000:65536` in `/etc/subuid`/`/etc/subgid`) rather than as host root.

The worker is dispatched as an **unprivileged** container with the minimal allowances rootless `dockerd` requires — `seccomp=unconfined`, `apparmor=unconfined`, and the `/dev/fuse` device for fuse-overlayfs — injected onto the container's `HostConfig` by `DockerWorkerOrchestrator`.
These allowances are set only when the persisted `WorkerImageConfiguration.InstallDocker` flag is true (read via `IGlobalSettingsQueries.GetWorkerImageInstallsDockerAsync`), so workers built without Docker get an identical run spec and entrypoint behaviour to before.

The entrypoint gates the whole concern behind `command -v dockerd-rootless.sh`: present (Docker image) means start the daemon, poll `docker version` against the nested socket until the API responds, then export `DOCKER_HOST`; absent (non-Docker image) means skip entirely.
`XDG_RUNTIME_DIR` is set unconditionally to `$HOME/.runtime` (mode 0700) — the canonical `/run/user/<uid>` path is root-owned and the unprivileged `node` user (uid 1000) cannot create directories there.
Ryuk stays enabled — Testcontainers .NET derives the reaper's socket bind-mount from the `unix://` `DOCKER_HOST`, so cleanup resolves under rootless with no extra configuration.

The daemon is best-effort — success exports `DOCKER_HOST` (the runtime capability contract); failure is non-fatal and surfaces the `FOUNDRY_DOCKER_UNAVAILABLE` log sentinel.
The entrypoint logs the condition, leaves `DOCKER_HOST` unset, and the worker continues in degraded mode.
This handles hosts where nested rootless DinD cannot start — Docker Desktop / WSL2 where `newuidmap … uid_map … Operation not permitted` — without aborting the run.
Reference: issue #221.

## Considered Options

- **Mount the host `/var/run/docker.sock` (Docker-out-of-Docker)** — rejected: it grants root-equivalent host access to processes running arbitrary code; a `POST /containers/create` with a bind-mount is a documented full host escape, which a socket proxy cannot mitigate. `HostPathSecurity` already blocks mounting the host socket, and this decision keeps that guarantee.
- **Privileged DinD (`--privileged`)** — rejected as the destination: a privileged container is not a security boundary and remains host-escapable.
- **CI owns the integration tier** — rejected as the *sole* model: it breaks integration-first TDD, since the red-green-refactor loop cannot round-trip through CI. The degraded path deliberately defers integration execution to CI when no local daemon is available — the inner TDD loop stays unit-based so it never depends on Docker.
- **Sysbox runtime** — deferred as the future upgrade path: it offers cleaner isolation without the `unconfined` allowances, but adds a host-runtime dependency that conflicts with Foundry being deployable as a plain container.
- **vfs storage driver** — recorded as the device-free fallback: it needs no `/dev/fuse` but copies whole layers per container, too slow for a Testcontainers suite, so fuse-overlayfs is the default and vfs the escape hatch where `/dev/fuse` cannot be exposed.

## Consequences

- The host (or runtime) where workers are dispatched must permit `seccomp=unconfined`, `apparmor=unconfined`, and expose `/dev/fuse`; where the daemon cannot start (allowances denied, or host kernel cannot run nested rootless DinD — e.g. Docker Desktop / WSL2 where `newuidmap … uid_map … Operation not permitted`), the entrypoint logs the condition, leaves `DOCKER_HOST` unset, and the worker continues in degraded mode — running its unit-test TDD loop and authoring integration tests that execute where a daemon exists (CI / native-Linux host). The run completes normally and is NOT a failure.
- Some kernels gate unprivileged user namespaces behind the host sysctl `kernel.apparmor_restrict_unprivileged_userns`; `apparmor=unconfined` covers the in-container side, but the host sysctl is out of scope here.
- `XDG_RUNTIME_DIR` is `$HOME/.runtime` rather than `/run/user/<uid>` — the unprivileged `node` user (uid 1000) cannot create directories under root-owned `/run`, so the runtime dir lives under `$HOME` where `node` has write access.
- Waiting for the daemon adds per-worker startup latency, accepted as the cost of integration-first TDD.
- AC2 and AC4 (a real suite passing against the nested daemon, with Ryuk cleanup) cannot be unit-tested — they require a real nested daemon and `/dev/fuse` — and are verified by the manual runbook below.
- The hardening tracked in #60 (cap-drop, seccomp profiles, read-only fs) and #61 (network isolation) must be reconciled with Ryuk's needs: an over-tight profile can block the reaper.

## Manual Acceptance Runbook

This verifies AC2 and AC4, which have no automated coverage.

1. Build a worker image with Docker enabled: `docker build --build-arg INSTALL_DOCKER=true --build-arg INSTALL_DOTNET=true -t foundry-worker:dind workers/`.
2. Dispatch (or `docker run`) a worker against a repository whose integration tests use Testcontainers (e.g. a suite using `Testcontainers.MsSql`), with the rootless allowances: `--security-opt seccomp=unconfined --security-opt apparmor=unconfined --device /dev/fuse`.
3. Confirm the entrypoint logs `Rootless dockerd is ready` before tests run, and that `dotnet test` creates containers against the nested daemon and the integration suite passes (AC2).
4. After the suite finishes, confirm Ryuk has removed the spawned containers — `docker -H "$DOCKER_HOST" ps -a` inside the worker shows no leftover test or Ryuk containers (AC4).
5. Confirm the host daemon is untouched: the worker never mounted `/var/run/docker.sock`, and host `docker ps` shows only the worker container itself (AC3).
