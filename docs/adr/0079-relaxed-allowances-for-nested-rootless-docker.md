---
reaches: non-functional
---

# Nested Rootless Docker for Workers, Under Relaxed Run-Spec Allowances

## Context

Workers run arbitrary repository code, including Testcontainers-based integration tests that need a Docker daemon (e.g. `Testcontainers.MsSql`).
Integration-first TDD requires the red-green-refactor loop to run inside the worker, so the daemon must be reachable locally rather than deferred to CI.
A worker is itself a container running untrusted, model-driven code, so however it reaches a daemon becomes a host-escape surface that must be bounded.

The first design gave workers their own rootless `dockerd` under a minimal allowance set (`seccomp=unconfined`, `apparmor=unconfined`, `/dev/fuse` for fuse-overlayfs). Spikes on Docker Desktop / WSL2 (kernel 6.18) with the shipped `foundry-worker` image showed that this set never reaches a usable daemon. It also fails on several points that do not depend on the host:

- `newuidmap: write to uid_map failed: Operation not permitted` until `CAP_SYS_ADMIN` is in the bounding set.
- `rootlesskit` needs `ip` (iproute2) and `sysctl` (procps), which the image did not install, and a `tap` device from `/dev/net/tun`.
- `sysctl net.ipv4.ip_forward` fails on the read-only `/proc/sys` until the system paths are unmasked.
- Nested containers fail to mount (overlay on overlay, `invalid argument`) while the daemon's data-root sits on the worker's own overlay filesystem.

The CI smoke test stubbed both `dockerd-rootless.sh` and `docker`, and the runtime acceptance criteria were covered only by a manual runbook, so none of this surfaced. The claim that nested rootless DinD cannot start on Docker Desktop / WSL2 was likewise a symptom of the minimal set, not of the host.

## Decision

Workers that get Docker run their **own rootless `dockerd`** started by the entrypoint. The host Docker daemon is never mounted or reachable, so a worker escape lands as the unprivileged `node` user (UID 1000) mapped into the host's subordinate-ID range (`node:100000:65536` in `/etc/subuid`/`/etc/subgid`) rather than as host root. The worker is never `--privileged`.

**Who gets a daemon.** Docker is installed in every worker image ([ADR 0109](0109-the-worker-image-installs-every-toolchain-and-reports-what-it-has.md)). A run gets the allowance set below and a daemon only when its capability profile is `Full` and the operator's nested-Docker switch is on ([ADR 0110](0110-nested-docker-privileges-stay-an-operator-switch.md)); Foundry signals that to the entrypoint with `FOUNDRY_NESTED_DOCKER=1`. `Restricted` runs never get them. The Settings page states the security cost beside the switch.

**The allowance set.** `RootlessDindRunSpec` is the single source. It layers on the hardened worker baseline of [ADR 0088](0088-worker-containers-run-hardened-by-default.md) rather than on Docker's defaults, so the root filesystem stays read-only with its writable mounts:

- capabilities: `CapDrop: [ALL]` with `CapAdd: [SETUID, SETGID, DAC_OVERRIDE, SYS_ADMIN]` — `SETUID`/`SETGID` serve the entrypoint's drop to `node` and the setuid `newuidmap`/`newgidmap`, which also need `DAC_OVERRIDE` to open `/proc/<pid>/uid_map` and `SYS_ADMIN` to write it
- security opts `seccomp=unconfined` and `apparmor=unconfined`, replacing the baseline's opts (`no-new-privileges` would stop `newuidmap`/`newgidmap` from gaining their capabilities, and the default seccomp profile blocks the `keyctl` session keyring the nested runc creates)
- unmasked system paths: `RootlessDindRunSpec.UnmaskSystemPaths` sets `WorkerContainerSpec.UnmaskSystemPaths`, which `DockerWorkerOrchestrator` maps to empty `HostConfig.MaskedPaths` and `HostConfig.ReadonlyPaths`, so the daemon can write `/proc/sys`. The docker CLI's `--security-opt systempaths=unconfined` is a client-side alias for this; the Engine API rejects it in `SecurityOpt`
- the device `/dev/net/tun`
- the daemon's data-root (`/home/node/.local/share/docker`) on a named per-run volume (`foundry-worker-docker-<runId>`), labelled for the run so orphan reconciliation can reap it

There is no separate "relaxed" flag. No host has been shown to work with the narrower set, so a two-flag design would let the operator pick a configuration that cannot work.

The image installs `iproute2` and `procps`. It strips the setuid bit from every binary except `newuidmap`/`newgidmap`, and does not install `fuse-overlayfs`. With the data-root on a volume, the nested daemon uses native overlayfs, so no `/dev/fuse` is needed.

**The entrypoint.** For a run that gets Docker, the entrypoint first sweeps any inherited `DOCKER_HOST` and `TESTCONTAINERS_*` values, then starts `dockerd-rootless.sh` in the background and polls `docker version` against the nested socket until the API responds. `XDG_RUNTIME_DIR` is `<home>/.runtime` (mode 0700), with the home read from the passwd database rather than the environment — the canonical `/run/user/<uid>` path is root-owned and `node` cannot create directories there. On success it exports `DOCKER_HOST=unix://$XDG_RUNTIME_DIR/docker.sock` (the runtime capability contract) and `TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1`. The override is required: inside a worker Testcontainers resolves the container host to the default bridge gateway (`172.17.0.1`), which under rootless DinD lives in rootlesskit's network namespace and refuses connections to published ports; IPv4 loopback is used rather than the name `localhost`, which may resolve to `::1` first. Ryuk stays enabled — Testcontainers .NET derives the reaper's socket bind-mount from the `unix://` `DOCKER_HOST`. Any other run only sweeps the inherited Docker environment.

**Best-effort daemon.** Failure to start is non-fatal: the entrypoint logs the condition and the `FOUNDRY_DOCKER_UNAVAILABLE` sentinel, leaves `DOCKER_HOST` unset, and the worker continues in degraded mode.

The isolation boundary is: the worker runs as `node` (uid 1000) with no route to container root. If code inside the worker escalates to root, it can reach the Docker host: `CAP_SYS_ADMIN`, no seccomp and a writable `/proc/sys` make that a host escape. On Docker Desktop, the host is the WSL VM, which sees the Windows drives. Stripping setuid and dropping every capability outside the set above narrow the routes to that escalation.

CI runs the real daemon, not stubs. It runs the built image with the allowance set's CLI flags, then asserts that a nested `docker run` succeeds and that a published port is reachable from the worker. The smoke step adds `SYS_ADMIN` to Docker's default capabilities rather than dropping to the four above, so it does not exercise the narrowed capability set.

## Considered Options

- **Mount the host `/var/run/docker.sock` (Docker-out-of-Docker)** — rejected: it grants root-equivalent host access to processes running arbitrary code; a `POST /containers/create` with a bind-mount is a documented full host escape, which a socket proxy cannot mitigate. `HostPathSecurity` already blocks mounting the host socket, and this decision keeps that guarantee.
- **Privileged DinD (`--privileged`)** — rejected: a privileged container is not a security boundary and remains host-escapable. The relaxed set grants no host devices beyond `/dev/net/tun` and keeps every other capability dropped.
- **CI owns the integration tier** — rejected as the *sole* model: it breaks integration-first TDD, since the red-green-refactor loop cannot round-trip through CI. The degraded path deliberately defers integration execution to CI when no local daemon is available — the inner TDD loop stays unit-based so it never depends on Docker.
- **The minimal set (`seccomp`/`apparmor` unconfined plus `/dev/fuse`), accepting degraded mode on Docker Desktop** — rejected, because it never produced a working daemon on any host that has been tested.
- **Separate opt-in flag for the relaxed set** — rejected for the reason above.
- **Sysbox / Docker Desktop Enhanced Container Isolation** — preferred long-term destination, since it isolates without unconfined allowances. Sysbox adds a host-runtime dependency that conflicts with Foundry being deployable as a plain container and does not run on Docker Desktop; ECI requires a Docker Business subscription.
- **vfs storage driver** — recorded as a device-free fallback: it copies whole layers per container, too slow for a Testcontainers suite. Native overlayfs on the data-root volume made it unnecessary.

## Consequences

- The host (or runtime) where workers are dispatched must permit the allowance set above. Where the daemon cannot start (allowances denied, or the host kernel cannot run nested rootless DinD), the worker continues in degraded mode — running its unit-test TDD loop and authoring integration tests that execute where a daemon exists (CI / native-Linux host). The run completes normally and is NOT a failure.
- Some kernels gate unprivileged user namespaces behind the host sysctl `kernel.apparmor_restrict_unprivileged_userns`; `apparmor=unconfined` covers the in-container side, but the host sysctl is out of scope here (CI relaxes it on its runners).
- Waiting for the daemon adds per-worker startup latency, accepted as the cost of integration-first TDD.
- Each run re-pulls its Testcontainers images into a fresh volume. Docker Hub's anonymous limit, read from the live `ratelimit-limit` header on 2026-10-28, was 100 pulls per hour per IP. A registry mirror is the remedy if concurrency grows.
- The nested daemon and its containers count against the worker's PID and memory limits. Measured: the daemon uses about 35 PIDs and 290 MB, and three Postgres containers bring it to 88 PIDs and 1 GB, within the 512 / 8 GB defaults.
- Ryuk reaping on the nested socket was verified: a labelled container was removed after the reaper's session ended. CI covers the daemon and published-port reachability, but a real Testcontainers suite with Ryuk cleanup is verified only by the manual runbook below.

## Manual Acceptance Runbook

This verifies a real Testcontainers suite and Ryuk cleanup against the nested daemon, which have no automated coverage.

1. Build the worker image: `docker build -t foundry-worker:local workers/`.
2. Turn on nested Docker in Settings and dispatch a `Full` run against a repository whose integration tests use Testcontainers (e.g. a suite using `Testcontainers.MsSql`). A hand-run `docker run` must carry the same set the orchestrator emits: `--cap-drop ALL --cap-add SETUID --cap-add SETGID --cap-add DAC_OVERRIDE --cap-add SYS_ADMIN --security-opt seccomp=unconfined --security-opt apparmor=unconfined --security-opt systempaths=unconfined --device /dev/net/tun -v <volume>:/home/node/.local/share/docker`, plus `FOUNDRY_NESTED_DOCKER=1`.
3. Confirm the entrypoint logs `Rootless dockerd is ready` before tests run, that `TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1` is exported alongside `DOCKER_HOST`, and that `dotnet test` creates containers against the nested daemon and the integration suite passes.
4. After the suite finishes, confirm Ryuk has removed the spawned containers — `docker -H "$DOCKER_HOST" ps -a` inside the worker shows no leftover test or Ryuk containers.
5. Confirm the host daemon is untouched: the worker never mounted `/var/run/docker.sock`, and host `docker ps` shows only the worker container itself.

## Revisions
- 2026-06-24 — Daemon startup became best-effort: failure is non-fatal, surfaces `FOUNDRY_DOCKER_UNAVAILABLE`, and the worker continues in degraded mode (#221).
- 2026-10-28 — The minimal allowance set, the fuse-overlayfs default and the claim that Docker Desktop / WSL2 cannot run nested rootless DinD were false from the start; replaced by the relaxed set, native overlayfs on a data-root volume, and a real-daemon CI smoke test.
- 2026-10-28 — `systempaths=unconfined` was false from the start as an Engine API value; the unmask is expressed as empty `MaskedPaths`/`ReadonlyPaths` via `UnmaskSystemPaths`.
- 2026-11-03 — The claim that Ryuk resolves under rootless with no extra configuration was false from the start; the entrypoint exports `TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1` alongside `DOCKER_HOST`.
- 2026-11-04 — The allowance set layers on the hardened baseline: `CapDrop: [ALL]` with `CapAdd: [SETUID, SETGID, DAC_OVERRIDE, SYS_ADMIN]` and a read-only root filesystem ([ADR 0088](0088-worker-containers-run-hardened-by-default.md)).
- 2026-11-14 — Docker is installed unconditionally and the allowances follow the run's capability profile instead of the `InstallDocker` toggle ([ADR 0109](0109-the-worker-image-installs-every-toolchain-and-reports-what-it-has.md)).
- 2026-11-14 — An operator switch, separate from the image, decides whether `Full` runs get the allowances, with the security cost stated beside it ([ADR 0110](0110-nested-docker-privileges-stay-an-operator-switch.md)).
- 2026-11-23 — Folded in the former record of the rootless Docker-in-Docker decision (host socket and `--privileged` rejected, best-effort daemon, `XDG_RUNTIME_DIR`, Ryuk) so one ADR states the whole decision (#1227).
