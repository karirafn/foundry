---
reaches: non-functional
---

# Worker Policy Lives in Root-Rendered Managed Settings

## Context

A worker runs `claude --dangerously-skip-permissions` as `node`, and Foundry's guardrails (the deny list, the merged host hooks) were written to `~/.claude/settings.json`, a file `node` owns. In OAuth mode that directory is the shared credential volume. Claude Code watches user, project, local and managed settings files and reloads `permissions` and `hooks` mid-session, so a worker could rewrite or strip its own guardrails with a single `Bash` call and, through the shared volume, affect every concurrent and later worker.

## Decision

Foundry's generated worker settings — the deny list, `model`, and `attribution` (`commit` and `pr` empty, `sessionUrl` false) — are written to Claude Code's Linux managed-settings path, `/etc/claude-code/managed-settings.json`, which outranks user, project, local and `--settings` values. Deny lists and hooks merge across scopes, and hooks from managed settings cannot be removed or disabled from any other file.

- **Root phase, then drop** — the worker image starts as root. The entrypoint branches on `id -u`: as root it renders the managed file (directory `0755`, file `0644`, both `root:root`) and re-executes itself through `setpriv --reuid=node --regid=node --init-groups --inh-caps=-all`. The root phase runs with only `SETUID`/`SETGID` (plus the rootless Docker additions) under `no-new-privileges` ([ADR 0088](0088-worker-containers-run-hardened-by-default.md)); the bounding set is not narrowed further because rootless dockerd relies on the setuid `newuidmap`/`newgidmap`. The root filesystem is read-only, so the managed file is written to a root-owned tmpfs at `/etc/claude-code`. Nothing else — clone, dockerd, `claude` — runs before the drop.
- **Readable by `node`** — the file is world-readable. Claude Code starts without a managed source it cannot read, so a root-only file would silently drop the policy.
- **Invariant check as `node`** — the non-root branch verifies the managed file exists, is owned by uid 0 and carries no group or other write bit, and fails bootstrap (stage `managed-settings`) otherwise. This also catches a container started directly as `node`, bypassing the root phase.
- **Fail closed** — a non-root start without a valid managed file, or an empty `CLAUDE_SETTINGS_JSON` fails bootstrap before the drop; a worker never runs without its guardrails.
- **User file cleared** — the entrypoint no longer writes `~/.claude/settings.json`, and deletes it on every start, so settings a worker injects into the shared volume never outlive the next worker start.
- **`allowManagedHooksOnly` stays unset** — hooks in the target repository's own `.claude/settings.json` keep running; managed hooks are already tamper-proof without it.

`model` in managed settings is a starting default, not a lock (locking would need `availableModels`); it moves with the rest only so Foundry writes one generated file.

## Considered Options

- **Keep the policy in `~/.claude/settings.json`** — rejected: the worker owns the file, and hot-reload applies its edits mid-session.
- **Pass the policy as `claude --settings '<json>'`** — rejected: a command-line argument is tamper-proof and deny lists merge, but `disableAllHooks: true` in any non-managed file disables every non-managed hook, so a worker could silently switch off hooks delivered this way.
- **Write the file from the API through the Docker archive API before start** — rejected: it would avoid root in the container, but the daemon refuses archive writes into a container whose root filesystem is read-only (`container rootfs is marked read-only`, Docker 29.9.3), and the `/etc/claude-code` tmpfs only exists once the container runs (ADR 0088). Its original reason, that the API could not read the host settings file for the hook merge, ended with host hooks ([ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md)).
- **Pre-populate a per-run volume before start (as the Publisher does for the Hand-off volume, [ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)) and mount it read-only at `/etc/claude-code`** — rejected: it needs a second per-run volume or a subpath mount, and the uid-0 invariant check becomes a read-only-mount check, because the Publisher writes as a non-root user. The gain is limited to workers without nested Docker dropping `SETUID`/`SETGID`; nested-Docker workers keep both for `newuidmap`/`newgidmap`, and the root phase already runs under `no-new-privileges` doing nothing but the render.
- **Have the dispatcher create the container as `User = "root"` and keep `USER node` in the image** — rejected: it splits the privilege boundary between C# and the image, and a manual `docker run` takes a different path from production.
- **A test-only switch to skip the managed-settings check** — rejected: it is a runtime bypass of the guardrail; the bats suite fakes `stat` instead, and one in-image test proves the real ownership and mode.

## Consequences

- The worker image's default user is root. Container hardening (capability dropping, seccomp, read-only rootfs, non-root user) must preserve the root render-then-drop phase or move the render out of the container.
- The shared OAuth config directory still exposes `CLAUDE.md`, `skills/`, `agents/`, `commands/` and `~/.claude.json` to cross-worker writes; this decision does not close that. [ADR 0090](0090-workers-share-only-the-oauth-credential-store.md) closes it for workers.
- The `attribution` setting removes Claude Code's own attribution instruction from the prompt, but it cannot guarantee that a worker adds no AI attribution: the model can still write a `Co-Authored-By` trailer or a "Generated with Claude Code" footer itself, as a worker with no git identity configured did on PR #808. Only code that strips attribution from commit messages and pull request text can guarantee its absence.

## Revisions
- 2026-11-04 — The root phase runs with only `SETUID`/`SETGID` (plus the rootless Docker additions) under `no-new-privileges` and writes the managed file to a root-owned tmpfs on a read-only root filesystem ([ADR 0088](0088-worker-containers-run-hardened-by-default.md)).
- 2026-11-05 — The list of generated settings now includes `attribution`, which `WorkerSettingsBuilder` gained after this ADR was written.
- 2026-11-06 — Host hooks are removed, so the host-hook merge and its fail-closed case no longer apply and the archive-API option is open for reconsideration ([ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md)).
- 2026-11-10 — The claim that the `attribution` setting means a worker never adds AI attribution was false from the start; managed settings reduce attribution but cannot guarantee its absence.
- 2026-11-27 — Reconsidered moving the render out of the container: the archive API is refused on a read-only root filesystem, and a pre-populated read-only volume costs more than the two capabilities it would drop, so the root phase stays (#740).
