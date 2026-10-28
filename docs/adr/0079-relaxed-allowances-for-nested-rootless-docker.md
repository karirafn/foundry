---
status: accepted
---

# Relaxed Run-Spec Allowances for Nested Rootless Docker

## Context

[ADR 0021](0021-rootless-docker-in-docker-for-worker-tests.md) gives Docker-enabled workers their own rootless `dockerd` under a minimal allowance set (`seccomp=unconfined`, `apparmor=unconfined`, `/dev/fuse`). This ADR replaces 0021's allowance-set clause, its fuse-overlayfs storage default, and its claim that nested rootless DinD cannot start on Docker Desktop / WSL2. 0021's rejection of the host socket and of `--privileged` still stands.

Spikes on Docker Desktop / WSL2 (kernel 6.18) with the shipped `foundry-worker` image showed that the 0021 set never reaches a usable daemon. It also fails on several points that do not depend on the host:

- `newuidmap: write to uid_map failed: Operation not permitted` until `CAP_SYS_ADMIN` is in the bounding set.
- `rootlesskit` needs `ip` (iproute2) and `sysctl` (procps), which the image did not install, and a `tap` device from `/dev/net/tun`.
- `sysctl net.ipv4.ip_forward` fails on the read-only `/proc/sys` until `systempaths=unconfined`.
- Nested containers fail to mount (overlay on overlay, `invalid argument`) while the daemon's data-root sits on the worker's own overlay filesystem.

The CI smoke test stubbed both `dockerd-rootless.sh` and `docker`, and 0021's runtime acceptance criteria were covered only by a manual runbook, so none of this surfaced.

## Decision

`WorkerImageConfiguration.InstallDocker` is the single opt-in. When it is true, the worker runs with the relaxed allowance set:

- `--cap-add SYS_ADMIN`
- `--security-opt seccomp=unconfined`, `apparmor=unconfined` and `systempaths=unconfined`
- `--device /dev/net/tun`
- the daemon's data-root (`/home/node/.local/share/docker`) on a named per-run volume, labelled for the run so orphan reconciliation can reap it

There is no separate "relaxed" flag. No host has been shown to work with the narrower 0021 set, so a two-flag design would let the operator pick a configuration that cannot work. The settings toggle states the security cost inline.

The image (when `INSTALL_DOCKER=true`) installs `iproute2` and `procps`. It strips the setuid bit from every binary except `newuidmap`/`newgidmap`, and no longer installs `fuse-overlayfs`. With the data-root on a volume, the nested daemon uses native overlayfs, so `/dev/fuse` is dropped from the run spec.

The isolation boundary becomes: the worker runs as `node` (uid 1000) with no route to container root. If code inside the worker escalates to root, it can reach the Docker host: `CAP_SYS_ADMIN`, no seccomp and a writable `/proc/sys` make that a host escape. On Docker Desktop, the host is the WSL VM, which sees the Windows drives. Stripping setuid narrows the routes to that escalation, and exact capability dropping remains with the container-hardening work.

CI runs the real daemon, not stubs. It runs the built image with the same allowance set the orchestrator emits, then asserts that a nested `docker run` succeeds and that a published port is reachable from the worker.

## Considered Options

- **Keep the 0021 set and accept degraded mode on Docker Desktop**: rejected, because the 0021 set never produced a working daemon on any host that has been tested.
- **Separate opt-in flag for the relaxed set**: rejected for the reason above.
- **Sysbox / Docker Desktop Enhanced Container Isolation**: preferred long-term destination, since it isolates without unconfined allowances. Sysbox does not run on Docker Desktop, and ECI requires a Docker Business subscription.
- **`--privileged`**: still rejected. The relaxed set grants no host devices and keeps the default capability set otherwise.

## Consequences

- Each run re-pulls its Testcontainers images into a fresh volume. Docker Hub's anonymous limit, read from the live `ratelimit-limit` header on 2026-10-28, was 100 pulls per hour per IP. A registry mirror is the remedy if concurrency grows.
- The nested daemon and its containers count against the worker's PID and memory limits. Measured: the daemon uses about 35 PIDs and 290 MB, and three Postgres containers bring it to 88 PIDs and 1 GB, within the 512 / 8 GB defaults.
- Ryuk reaping on the nested socket was verified: a labelled container was removed after the reaper's session ended.
- Hosts where the daemon still cannot start keep 0021's degraded path (`FOUNDRY_DOCKER_UNAVAILABLE`, `DOCKER_HOST` unset).
