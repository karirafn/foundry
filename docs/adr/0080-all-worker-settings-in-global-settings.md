---
reaches: structure
---

# All Worker Settings in GlobalSettings, Database as the Only Source

## Context

An earlier change moved the worker image toolchain flags into `GlobalSettings` but deliberately kept the other worker settings — image tag, mounts, CPU/memory/PID limits, Claude Code settings (model, deny rules, hooks), branch naming, and the prompt-template fallbacks — in `IConfiguration` (`Workers` section of appsettings plus user secrets).
Operators run Foundry as a container, so a value only reachable by editing appsettings and restarting is effectively unreachable, and the prompt templates already showed the cost of mixing sources: the DB value is nullable and silently falls back to config (`IssueClaimedHandler.cs:161-162`), so the UI shows an empty field while a different template runs, and the DB copy skips the `{issueNumber}` check that `WorkerOptionsValidator` applies to the config copy.

## Decision

Every operator-facing `Workers` setting moves into `GlobalSettings`, and the database is the only source — `WorkerOptions`, its validator, and the `Workers` configuration section are removed.

- **Seeding** — code defaults seed the row; every field is non-null, so the UI always shows the value in effect.
- **Shape** — one value object per group, each persisted as a JSON column and owning the invariants `WorkerOptionsValidator` held: `WorkerImageConfiguration` (image tag, build toggle), `WorkerResourceLimits` (memory, CPU, PIDs), and `ClaudeCodeSettings` (additional deny rules). Host mounts and host hooks are removed ([ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md)); role prompts and branch naming ship in the worker image and each role has its own model setting ([ADR 0095](0095-worker-roles-ship-in-the-worker-image.md)); there is no CI/CD edit blocking, so workers may change CI/CD files and human review is the gate ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)).
- **Image changes** — one `WorkerImageSettingsChanged` event covers the tag and the build toggle: with build enabled it requests a rebuild; with build disabled it inspects the tag and a missing image lands in `ImageBuildState.Failed`, reusing the existing dispatch gate, banner, and retry. Resource limits are read per dispatch with no rebuild, so changing them raises no `WorkerImageSettingsChanged`.

Not moved: bootstrap settings (connection string, DataProtection key path, logging, `AllowedHosts`, OTLP endpoint, Aspire dashboard keys), which are needed before the database is readable or are framework-consumed; and pure internal tuning, which becomes code constants — `OutboxOptions`, the image build `ContextPath` (a packaging fact), and the image build backoffs (the manual retry is the escape hatch).

## Considered Options

- **Config seeds the row on first start** — rejected: keeps an `IConfiguration` read path that only executes on an empty database, so it is rarely exercised and easily forgotten.
- **Config overrides the database** — rejected: two sources of truth; the UI would show values that are not in effect, the defect the prompt templates already exhibit.
- **A separate `WorkerSettings` aggregate** — rejected: a second single-row aggregate in the same module adds a parallel persistence path for no boundary benefit.
- **Editable CI/CD deny list seeded from defaults** — rejected: freezes the default list per install and makes partial accidental deletion easy.
- **Hooks edited in the UI** — rejected: duplicates the operator's host Claude Code configuration, which must then be kept in sync by hand.
- **Wait for WebApi authentication (#178)** — rejected as a blocker: the API binds to localhost in development and deployment is already gated on #178.

## Consequences

- Overturns the "mounts and resource limits remain infrastructure" note in DOMAIN.md.
- Worker settings, including the deny rules, are writable through an unauthenticated API until #178 lands — a widened exposure recorded on #178.
- Existing values in user secrets are not imported; they are re-entered through the UI.
- Foundry's generated worker settings are rendered into a root-owned `managed-settings.json` rather than the worker's writable `~/.claude/settings.json` ([ADR 0086](0086-worker-policy-in-root-rendered-managed-settings.md)).

## Revisions
- 2026-11-02 — The Shape clause's `WorkerContainerSpec` was refined: the image tag ships on `WorkerImageConfiguration` (#625), the limits on `WorkerResourceLimits` and the mounts on `WorkerMounts` (#604), and limit and mount changes raise no `WorkerImageSettingsChanged`.
- 2026-11-06 — The Hooks clause and `WorkerMounts` are removed ([ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md)).
- 2026-11-06 — `WorkerPrompts` and the single `model` field are replaced by role prompts in the worker image and a per-role model setting ([ADR 0095](0095-worker-roles-ship-in-the-worker-image.md)).
- 2026-11-09 — The `BlockCiCdEdits` toggle and the CI/CD deny rules clause are removed; workers may change CI/CD files and human review is the gate ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)).
