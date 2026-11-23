---
reaches: non-functional
---

# Worker Containers Run Hardened by Default

## Context

A worker runs an autonomous agent with `--dangerously-skip-permissions` against untrusted repository content. Before this decision it ran with Docker's default capability set (14 capabilities, including `CHOWN`, `DAC_OVERRIDE`, `FOWNER`, `NET_RAW`, `MKNOD`, `SETFCAP` and `SETPCAP`), a writable root filesystem and no `no-new-privileges`. [ADR 0086](0086-worker-policy-in-root-rendered-managed-settings.md) made the image start as root so its entrypoint can render the root-owned `/etc/claude-code/managed-settings.json` and then drop to `node` through `setpriv`. Hardening has to keep that render-then-drop phase working, and it also has to keep the rootless Docker-in-Docker allowance set of [ADR 0079](0079-relaxed-allowances-for-nested-rootless-docker.md) working.

## Decision

`WorkerHardeningSpec` is the single source of the baseline, and `IssueClaimedHandler` applies it to every worker spec. `DockerWorkerOrchestrator` maps it onto `HostConfig`.

- **Capabilities**: `CapDrop: [ALL]` and `CapAdd: [SETUID, SETGID]`. The root phase needs only these two, for `setpriv --reuid --regid --init-groups`. Writing the managed file needs no capability, because root owns the directory it writes to. Nothing runs with a capability after the drop: the uid is non-root and `setpriv` clears the inheritable set. What remains only bounds the set (`CapBnd` = `0xc0`).
- **`no-new-privileges:true`**: a setuid or file-capability binary cannot regain anything that was dropped.
- **Seccomp**: the daemon's default profile. It applies whenever no `seccomp=` option is passed, so Foundry passes none. A Foundry-maintained copy of the profile was rejected because it would drift from the daemon's.
- **Read-only root filesystem** (`ReadonlyRootfs`), with these writable paths:
  - tmpfs at `/etc/claude-code` (`mode=0755`, `noexec`, root-owned), the only tmpfs. Only the root phase can write the managed settings there, and the ADR 0086 invariant (uid 0, no group or other write bit) still holds.
  - Anonymous volumes at `/workspace`, `/home/node`, `/tmp` and `/var/tmp`. A clone, a package cache, a build output, or the worktrees, tool installs and caches agents put in the temp directories can exceed what memory-backed tmpfs should hold under the worker's memory limit. `/tmp` and `/var/tmp` inherit the image's sticky `1777` mode and keep `exec`, but have no `nosuid,nodev`; that is the same exposure `/workspace` and `/home/node` carry, and dropped `CHOWN` and `MKNOD` still stop a worker from creating a root-owned setuid file or a device node. An anonymous volume inherits the image's content and ownership (`node:node`) on first mount. `IDockerContainerRuntime.RemoveAsync` passes `RemoveVolumes`, so these volumes go with the container. Named volumes (credentials, the DinD data-root) are never removed this way.
- **Non-root user**: unchanged from ADR 0086. Everything after the drop (clone, auth, dockerd, `claude`) runs as `node`.

The rootless Docker-in-Docker profile (`RootlessDindRunSpec`, ADR 0079) now layers on this baseline instead of on Docker's defaults. It keeps `CapDrop: [ALL]`, the read-only root filesystem and the writable mounts, and it changes only:

- `CapAdd: [SETUID, SETGID, DAC_OVERRIDE, SYS_ADMIN]`. The setuid `newuidmap`/`newgidmap` need `DAC_OVERRIDE` to open the node-owned `/proc/<pid>/uid_map` and `SYS_ADMIN` to write it. Each one was shown to be necessary by removing it. `SETFCAP` was not needed. These two are the image's only setuid binaries, so nothing else running as `node` can raise these capabilities from the bounding set.
- `seccomp=unconfined` and `apparmor=unconfined` instead of `no-new-privileges`. `no-new-privileges` would stop `newuidmap`/`newgidmap` from gaining their capabilities. With the default seccomp profile the nested daemon starts, but every nested `docker run` fails: runc's `keyctl` session-keyring call is blocked.

The DinD set was verified on Docker 29 / WSL2 kernel 6.18: a nested rootless daemon started, ran a container and served a published port to the worker. The non-DinD set is verified by the `WhenStartedWithWorkerHardening` integration test against the real daemon. It checks `CapEff`, `NoNewPrivs`, `Seccomp: 2`, a read-only rootfs, each writable path, and that the scratch volumes are removed with the container.

## Considered Options

- **Move the managed-settings render out of the container so the image can start as `node`**: rejected. The main reason ADR 0086 gave, that the API cannot read the host settings file, no longer holds since [ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md) removed host hooks, so this option is open for reconsideration. The root phase is brief and holds only `SETUID`/`SETGID` under `no-new-privileges`. It does nothing but render the file, and keeps no capability after the drop.
- **tmpfs for `/workspace`, `/home/node`, `/tmp` and `/var/tmp`**: rejected. tmpfs pages count against the worker's memory limit, so a large clone or NuGet/npm cache would turn into an OOM kill, and an unsized tmpfs at `/tmp` got half the host's RAM, so a run that filled it aborted with "No space left on device" (#821).
- **A custom seccomp profile for DinD that allows `keyctl`**: deferred. It would have to track the daemon's default profile and needs a host matrix to validate. Sysbox or ECI (ADR 0079) remains the long-term route.
- **Drop the bounding set to nothing inside the entrypoint (`setpriv --bounding-set -all`)**: rejected. It needs `SETPCAP` in the container, and for DinD it would strip what `newuidmap`/`newgidmap` need. `no-new-privileges` already closes the setuid route for non-DinD workers.

## Consequences

- A worker can no longer write outside `/tmp`, `/var/tmp`, `/workspace`, `/home/node` and its volume mounts. Anything the image needs writable at runtime must live under one of those paths. `npm install -g` and the CLI auto-updater already failed for `node` and are unaffected.
- `ping` and other raw-socket tools no longer work in non-DinD workers (`NET_RAW` is dropped).
- A worker container removed outside Foundry without `-v` leaks its anonymous volumes. `docker volume prune` reclaims them.
- The DinD profile is narrower than ADR 0079's (Docker's 14 defaults plus `SYS_ADMIN` become 4), but `SYS_ADMIN` plus unconfined seccomp still make an escalation to root a host escape, as ADR 0079 states.
- The CI image smoke step still runs the DinD image with ADR 0079's CLI flags, so it does not exercise the narrowed set. A host where the narrowed set fails to start dockerd falls back to ADR 0079's degraded mode (`FOUNDRY_DOCKER_UNAVAILABLE`) rather than failing the run.

## Revisions
- 2026-11-06 — Host hooks and worker bind mounts are gone, so the main reason for keeping the render in the container no longer holds and the writable mounts are volume mounts only ([ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md)).
- 2026-11-11 — `/tmp` and `/var/tmp` became anonymous volumes instead of tmpfs, the tmpfs clause having been false from the start because tmpfs pages count against the memory limit; `/etc/claude-code` is the only tmpfs (#821).
