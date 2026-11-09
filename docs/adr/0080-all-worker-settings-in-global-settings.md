---
status: accepted
---

# All Worker Settings in GlobalSettings, Database as the Only Source

> **2026-11-02 — Amended:** the Decision's "`WorkerContainerSpec` (image tag, memory, CPU, PIDs)" shape clause is refined in implementation. The image tag ships on `WorkerImageConfiguration` (slice #625); the worker resource limits ship on a dedicated `WorkerResourceLimits` value object and the mounts on `WorkerMounts` (slice #604). Limit and mount changes are read per dispatch with no rebuild event, so they raise no `WorkerImageSettingsChanged`. The database-only, all-settings-in-`GlobalSettings` decision is unchanged.

> **2026-11-06 — Amended:** the Decision's Hooks clause and the `WorkerMounts` part of its Shape clause are replaced by [ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md), which removes host hooks and worker mounts.

> **2026-11-06 — Amended:** the `WorkerPrompts` part of the Decision's Shape clause (system and worker templates, branch naming, edited in the dashboard) is replaced by [ADR 0095](0095-worker-roles-ship-in-the-worker-image.md). Role prompts move into the worker image. The `model` field of `ClaudeCodeSettings` is also replaced by ADR 0095, which gives each role its own model setting.

> **2026-11-09 — Amended:** the `BlockCiCdEdits` toggle in the Decision's Shape clause and the CI/CD deny rules clause are replaced by [ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md), which removes CI/CD edit blocking; workers may change CI/CD files and human review is the gate.

## Context

[ADR 0019](0019-worker-image-flags-in-settings.md) moved the worker image toolchain flags into `GlobalSettings` but deliberately kept the other worker settings — image tag, mounts, CPU/memory/PID limits, Claude Code settings (model, deny rules, hooks), branch naming, and the prompt-template fallbacks — in `IConfiguration` (`Workers` section of appsettings plus user secrets).
Operators run Foundry as a container, so a value only reachable by editing appsettings and restarting is effectively unreachable, and the prompt templates already showed the cost of mixing sources: the DB value is nullable and silently falls back to config (`IssueClaimedHandler.cs:161-162`), so the UI shows an empty field while a different template runs, and the DB copy skips the `{issueNumber}` check that `WorkerOptionsValidator` applies to the config copy.

## Decision

Every operator-facing `Workers` setting moves into `GlobalSettings`, and the database is the only source — `WorkerOptions`, its validator, and the `Workers` configuration section are removed.

- **Seeding** — code defaults seed the row; every field is non-null, so the UI always shows the value in effect. Existing null prompt templates are backfilled with the defaults.
- **Shape** — one value object per group, each persisted as a JSON column and owning the invariants `WorkerOptionsValidator` held: `WorkerContainerSpec` (image tag, memory, CPU, PIDs), `WorkerMounts` (one table keyed on container path with a per-row read-only/writable flag, so a path in both lists is unrepresentable), `ClaudeCodeSettings` (model, `BlockCiCdEdits` toggle, additional deny rules), `WorkerPrompts` (system and worker templates, branch naming), and the image build toggle.
- **CI/CD deny rules** — stored as a boolean; the rule list stays a code constant so later additions reach existing installs instead of being frozen in a seeded snapshot.
- **Hooks** — sourced from the operator's host Claude Code `settings.json`, mounted read-only and merged into the worker settings by the entrypoint on every container start, minus an operator-maintained exclusion list. A configured but missing or invalid file is a bootstrap failure; no configured path means no host hooks.
- **Image changes** — one `WorkerImageSettingsChanged` event covers flags, tag, and build toggle: with build enabled it requests a rebuild; with build disabled it inspects the tag and a missing image lands in `ImageBuildState.Failed`, reusing the existing dispatch gate, banner, and retry.

Not moved: bootstrap settings (connection string, DataProtection key path, logging, `AllowedHosts`, OTLP endpoint, Aspire dashboard keys), which are needed before the database is readable or are framework-consumed; and pure internal tuning, which becomes code constants — `OutboxOptions`, the image build `ContextPath` (a packaging fact), and the image build backoffs (the manual retry is the escape hatch).

## Considered Options

- **Config seeds the row on first start** — rejected: keeps an `IConfiguration` read path that only executes on an empty database, so it is rarely exercised and easily forgotten.
- **Config overrides the database** — rejected: two sources of truth; the UI would show values that are not in effect, the defect the prompt templates already exhibit.
- **A separate `WorkerSettings` aggregate** — rejected for the reason ADR 0019 gave: a second single-row aggregate in the same module adds a parallel persistence path for no boundary benefit.
- **Editable CI/CD deny list seeded from defaults** — rejected: freezes the default list per install and makes partial accidental deletion easy.
- **Hooks edited in the UI** — rejected: duplicates the operator's host Claude Code configuration, which must then be kept in sync by hand.
- **Wait for WebApi authentication (#178)** — rejected as a blocker: the API binds to localhost in development and deployment is already gated on #178.

## Consequences

- Overturns ADR 0019's "mounts and resource limits remain infrastructure" clause and the matching DOMAIN.md note; DOMAIN.md is updated as each slice ships.
- Host mounts, hooks, and deny rules become writable through an unauthenticated API until #178 lands — a widened exposure recorded on #178.
- Existing values in user secrets (mounts, hooks, model, worker prompt) are re-entered through the UI once; there is no import.
- Foundry's generated worker settings still live in the worker's writable `~/.claude/settings.json`; moving the enforced subset to a root-owned `managed-settings.json` is tracked separately.
