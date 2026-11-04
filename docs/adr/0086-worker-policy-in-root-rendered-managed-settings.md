# Worker Policy Lives in Root-Rendered Managed Settings

## Context

A worker runs `claude --dangerously-skip-permissions` as `node`, and Foundry's guardrails (the deny list, the merged host hooks) were written to `~/.claude/settings.json`, a file `node` owns. In OAuth mode that directory is the shared credential volume. Claude Code watches user, project, local and managed settings files and reloads `permissions` and `hooks` mid-session, so a worker could rewrite or strip its own guardrails with a single `Bash` call and, through the shared volume, affect every concurrent and later worker.

## Decision

Foundry's generated worker settings — the deny list, `model`, and the host hooks merged from the operator's host settings file — are written to Claude Code's Linux managed-settings path, `/etc/claude-code/managed-settings.json`, which outranks user, project, local and `--settings` values. Deny lists and hooks merge across scopes, and hooks from managed settings cannot be removed or disabled from any other file.

- **Root phase, then drop** — the worker image starts as root. The entrypoint branches on `id -u`: as root it renders the managed file (directory `0755`, file `0644`, both `root:root`) and re-executes itself through `setpriv --reuid=node --regid=node --init-groups --inh-caps=-all`, keeping Docker's default capability bounding set because rootless dockerd relies on the setuid `newuidmap`/`newgidmap`. Nothing else — clone, dockerd, `claude` — runs before the drop.
- **Readable by `node`** — the file is world-readable. Claude Code starts without a managed source it cannot read, so a root-only file would silently drop the policy.
- **Invariant check as `node`** — the non-root branch verifies the managed file exists, is owned by uid 0 and carries no group or other write bit, and fails bootstrap (stage `managed-settings`) otherwise. This also catches a container started directly as `node`, bypassing the root phase.
- **Fail closed** — a non-root start without a valid managed file, an empty `CLAUDE_SETTINGS_JSON`, or a failed host-hook merge fails bootstrap before the drop; a worker never runs without its guardrails.
- **User file cleared** — the entrypoint no longer writes `~/.claude/settings.json`, and deletes it on every start, so settings a worker injects into the shared volume never outlive the next worker start.
- **`allowManagedHooksOnly` stays unset** — hooks in the target repository's own `.claude/settings.json` keep running; managed hooks are already tamper-proof without it.

`model` in managed settings is a starting default, not a lock (locking would need `availableModels`); it moves with the rest only so Foundry writes one generated file.

## Considered Options

- **Keep the policy in `~/.claude/settings.json`** — rejected: the worker owns the file, and hot-reload applies its edits mid-session.
- **Pass the policy as `claude --settings '<json>'`** — rejected: a command-line argument is tamper-proof and deny lists merge, but `disableAllHooks: true` in any non-managed file disables every non-managed hook, so a worker could silently switch off hooks delivered this way.
- **Write the file from the API through the Docker archive API before start** — rejected: it avoids root in the container, but the API cannot read the host settings file (its filesystem is not necessarily the Docker host's), so the hook merge must run inside the container.
- **Have the dispatcher create the container as `User = "root"` and keep `USER node` in the image** — rejected: it splits the privilege boundary between C# and the image, and a manual `docker run` takes a different path from production.
- **A test-only switch to skip the managed-settings check** — rejected: it is a runtime bypass of the guardrail; the bats suite fakes `stat` instead, and one in-image test proves the real ownership and mode.

## Consequences

- The worker image's default user is root. Container hardening (capability dropping, seccomp, read-only rootfs, non-root user) must preserve the root render-then-drop phase or move the render out of the container.
- The shared OAuth config directory still exposes `CLAUDE.md`, `skills/`, `agents/`, `commands/` and `~/.claude.json` to cross-worker writes; this decision does not close that.
