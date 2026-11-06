# Workers Receive No Host Claude Code Configuration

## Context

[ADR 0080](0080-all-worker-settings-in-global-settings.md) let operators bring host files into workers in two ways. Worker mounts bind-mounted any host directory (in practice skills, agents and commands folders). Host hooks mounted the host Claude Code `settings.json` and the entrypoint merged its `hooks` key into the managed settings. This ADR replaces two parts of 0080: its Hooks clause and the `WorkerMounts` part of its Shape clause.

Neither feature was in use. On 2026-11-06 the live database and every other local copy held no mounts and an empty host-settings path, and workers had been running without them. Both were still configurable through the unauthenticated WebApi (#178). The only check on which host paths could be mounted was a deny list of sensitive directories.

## Decision

Workers receive no configuration from the operator's host. Worker mounts and host hooks are removed completely, together with the bind-mount plumbing that only they used: `BindMount`, `HostPathSecurity`, `WorkerContainerSpec.BindMounts` and the orchestrator's `Binds` wiring. A worker's Claude Code configuration comes only from the image, the managed settings Foundry renders, and the target repository.

## Considered Options

- **Keep the features, unused, as an operator option**: rejected. Each one is a host-filesystem path into a sandbox that anyone can configure while the API has no authentication, and it keeps validation, entrypoint merge logic and UI that nothing uses.
- **Keep the bind-mount plumbing for a future feature**: rejected. With no producer it only ever carries an empty list. A future host-mount feature should bring it back deliberately, with its own security review.

## Consequences

- Operator skills, agents, commands and hooks do not apply inside workers. A worker prompt that calls an operator skill (for example `/implement`) runs as plain text.
- [ADR 0086](0086-worker-policy-in-root-rendered-managed-settings.md) and [ADR 0088](0088-worker-containers-run-hardened-by-default.md) rejected rendering managed settings from the API and starting the image as `node`, mainly because "the API cannot read the host settings file". That reason no longer applies, so the option is open for reconsideration.
- The `worker_mounts` and `host_hooks` columns are dropped.
