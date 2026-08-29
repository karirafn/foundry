# Read Credential Volume via an Ephemeral Helper Container

## Context

After a successful interactive OAuth login, Foundry must capture the authenticated account identity (email, org name, subscription type) written by the genuine Claude Code CLI.

The login container's entrypoint runs `exec claude auth login --claudeai`, so the CLI is PID 1.
Its lifecycle at the moment login succeeds is unreliable: the CLI emits `Login successful.` and then exits, but the exit is not synchronous with the signal — the container has been observed both already stopped and still running (living 14+ seconds past the signal) when success is detected.
So the login container cannot be relied on as a target for reading the persisted identity.

The original implementation ran `claude auth status --json` via `docker exec` on that same login container.
Whenever the container had already exited, `docker exec` failed with `Error response from daemon: container … is not running`, surfacing as an opaque `LoginFailureReason.Unknown` ("Sign in failed").
Depending on the login container's post-success state is fragile precisely because that state is nondeterministic.

## Decision

Read credential state from the credential **volume** via a fresh, short-lived helper container, decoupled from the login container's lifecycle.

Success is detected from the `Login successful.` log signal, not from the container exit code (which races the signal, as the Context explains); an `Invalid code` line or a stream that closes without the success signal is failure.
The Claude Code CLI writes `.credentials.json` to `$CLAUDE_CONFIG_DIR` (on the mounted credential volume) before it emits that signal, so the credential persists on the volume independently of the login container.
`DockerWorkerOrchestrator.GetCredentialVolumeAuthStatusAsync` starts a helper container (`foundry-claude-login` image, `sleep`, credential volume mounted), execs `claude auth status --json` inside it, then stops and removes the helper in a `finally`.
The helper mounts the volume **read-only** — the auth-status read never writes, and least privilege on a long-lived token store is worth the one-line cost.
This volume read is also the real confirmation that login persisted a valid credential.

This is the single, cross-platform way Foundry touches the credential volume: through a container, via the Docker socket — never via host filesystem access to the daemon's volume mountpoint (see [ADR 0029](0029-oauth-status-derived-from-persisted-state.md), which removes the host-FS reader for status display).

## Considered Options

### Exec into the login container (original)

Rejected: the login container's state when success is detected is nondeterministic (sometimes exited, sometimes still running), so exec-ing into it is inherently fragile — and it fails outright (`container … is not running`) whenever it has exited. This was the defect.

### Keep the login container alive after success (e.g. `claude auth login; sleep`)

Rejected: it leaves a container idle holding the volume for no benefit.
The credential is already durable on the volume, and success is detected from the `Login successful.` log signal rather than from container exit, so keeping it alive to exec into it buys nothing.

### Read the volume file from the host filesystem

Rejected: the daemon mountpoint (`/var/lib/docker/volumes/…`) is unreachable from the host on Docker Desktop (it lives inside the Linux VM), so a host-process read cannot see the file. See ADR 0029.

## Consequences

- Reading credential state costs a short-lived container start/stop. This is acceptable at login time (infrequent) but is the reason status **display** does not read the volume at all (ADR 0029).
- The mechanism is identical on Windows/Mac dev (host process + Docker socket) and Linux prod (containerized + Docker socket) — no per-platform branching.
- Login success no longer depends on the login container exiting, so the flow is robust whether the CLI exits promptly or lingers after signalling success.
- The behavior is exercised in the real-Docker `login-integration` CI job; unit tests cover both a login container that has exited and one still running when the success signal appears.
