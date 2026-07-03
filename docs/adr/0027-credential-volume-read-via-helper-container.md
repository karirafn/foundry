# Read Credential Volume via an Ephemeral Helper Container

## Context

After a successful interactive OAuth login, Foundry must capture the authenticated account identity (email, org name, subscription type) written by the genuine Claude Code CLI.

The login container's entrypoint runs `exec claude auth login --claudeai`, so the CLI is PID 1.
On a successful login the CLI completes and exits, which stops the container.
`WaitForLoginSuccessAsync` treats this exit (code 0) as the success signal — so by the time login has succeeded, the login container is already stopped.

The original implementation ran `claude auth status --json` via `docker exec` on that same login container.
`docker exec` against a stopped container fails with `Error response from daemon: container … is not running`, which surfaced as an opaque `LoginFailureReason.Unknown` ("Sign in failed") on every real login.

## Decision

Read credential state from the credential **volume** via a fresh, short-lived helper container, decoupled from the login container's lifecycle.

The Claude Code CLI writes `.credentials.json` to `$CLAUDE_CONFIG_DIR` (on the mounted credential volume) before it exits, so the credential persists on the volume independently of the login container.
`DockerWorkerOrchestrator.GetCredentialVolumeAuthStatusAsync` starts a helper container (`foundry-claude-login` image, `sleep`, credential volume mounted), execs `claude auth status --json` inside it, then stops and removes the helper in a `finally`.
The helper mounts the volume **read-only** — the auth-status read never writes, and least privilege on a long-lived token store is worth the one-line cost.

This is the single, cross-platform way Foundry touches the credential volume: through a container, via the Docker socket — never via host filesystem access to the daemon's volume mountpoint (see ADR 0029, which removes the host-FS reader for status display).

## Considered Options

### Exec into the login container (original)

Rejected: the login container has already exited on success, so exec fails. This was the defect.

### Keep the login container alive after success (e.g. `claude auth login; sleep`)

Rejected: it would break the exit-code-0 success signal that `WaitForLoginSuccessAsync` depends on, requiring a different success-detection mechanism, and would leave a container idle holding the volume for no benefit — the credential is already durable on the volume.

### Read the volume file from the host filesystem

Rejected: the daemon mountpoint (`/var/lib/docker/volumes/…`) is unreachable from the host on Docker Desktop (it lives inside the Linux VM), so a host-process read cannot see the file. See ADR 0029.

## Consequences

- Reading credential state costs a short-lived container start/stop. This is acceptable at login time (infrequent) but is the reason status **display** does not read the volume at all (ADR 0029).
- The mechanism is identical on Windows/Mac dev (host process + Docker socket) and Linux prod (containerized + Docker socket) — no per-platform branching.
- The behavior is exercised in the real-Docker `login-integration` CI job; unit tests use a fake orchestrator that must model exec failing on a stopped container so this class of bug cannot silently regress.
