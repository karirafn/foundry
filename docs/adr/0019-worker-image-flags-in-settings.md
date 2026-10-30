---
status: accepted
---

# Worker Image Flags in GlobalSettings with Rebuild-on-Save

> **2026-10-21 — Amended:** the `INSTALL_GLAB` / `INSTALL_GH` members of the `WorkerImageConfiguration`
> value object in Decision are removed by #580 — the provider CLIs (`gh`, `glab`) are now unconditional
> worker prerequisites baked into `Dockerfile.base`, and a missing CLI fails bootstrap at the entrypoint
> boundary. The remaining flags (`INSTALL_DOTNET`, `INSTALL_ANGULAR`, `INSTALL_CHROMIUM`, `INSTALL_DOCKER`)
> stay as described. This was overturned by later work, not false when written.

> **2026-10-30 — Amended:** the clause keeping mounts and CPU/PID/memory limits in `IConfiguration` (Decision,
> last paragraph; Consequences, first bullet) is replaced by [ADR 0080](0080-all-worker-settings-in-global-settings.md),
> which moves every operator-facing worker setting into `GlobalSettings`.

## Context

The worker image preinstall flags (`INSTALL_DOTNET`, `INSTALL_ANGULAR`, `INSTALL_GLAB`, `INSTALL_GH`) were free-form entries in `Workers:ImageBuild:BuildArgs` (`IConfiguration`), deliberately excluded from the DB-backed `GlobalSettings` aggregate — DOMAIN.md records "infrastructure-only settings (Docker image, mounts, memory/CPU/PID limits) remain in `IConfiguration`".

Changing which toolchains are baked into the worker image therefore required editing config and redeploying. The operator needs to pick toolchains from the dashboard and have the image rebuilt automatically. The image is built once at startup by `WorkerImageBuildService` ([ADR 0009](0009-worker-image-build-hosted-service.md)); there is no on-demand rebuild path.

## Decision

Move the four `INSTALL_*` flags into a `WorkerImageConfiguration` value object (four named bools) owned by `GlobalSettings`, persisted in the DB. The value object owns its change-equality and the mapping to Docker build-args. Typed bools make invalid or injected flag values unrepresentable, removing the `WorkerOptionsValidator` build-arg key/value checks that existed only because the source was a free-form string dictionary.

Saving changed flags raises `WorkerImageConfigurationChanged`, which invokes a reusable `RebuildWorkerImage` operation in the background: it sets `ImageBuildStatus` (`Idle` / `Building` / `Failed`) on `GlobalSettings`, builds with the persisted flags, and resolves to `Idle` on success or `Failed` on error. The manual retry button invokes the same operation. `WorkerDispatchService` withholds dispatch while status is `Building` or `Failed`, alongside the existing pause gate; running workers are unaffected because containers hold an immutable image ID. The startup build (ADR 0009) now reads the persisted flags from the DB and always rebuilds.

Other infrastructure settings (mounts, CPU/PID/memory limits) stay in `IConfiguration` — only the toolchain flags graduate to settings.

## Considered Options

- **Keep flags in `IConfiguration`** — rejected: every toolchain change needs a redeploy and there is no UI surface, defeating the goal.
- **Separate `WorkerImageSettings` aggregate** — rejected: the flags are cohesive single-row UI settings, and `GlobalSettings` is already the single-row home for UI-configurable state; a second aggregate adds a parallel persistence path for no boundary benefit.
- **Free-form editable Dockerfile in the UI** — rejected: an arbitrary-code-execution and broken-build surface where invalid states are trivially representable, the opposite of the typed-flag guard.
- **Resume dispatch on the previous image when a rebuild fails** — rejected: the operator changed flags expecting a toolchain that is not in the stale image; workers would fail far from the cause. Dispatch stays gated on `Failed` until retry.

## Consequences

- Overturns the DOMAIN.md "image settings stay in `IConfiguration`" note for these specific flags; mounts and resource limits remain infrastructure. DOMAIN.md is updated when the change is built.
- Introduces on-demand rebuild infrastructure and a dispatch gate keyed on `ImageBuildStatus`; the startup build now depends on the DB being readable before the first build.
- `Workers:ImageBuild:BuildArgs` is removed; first boot seeds defaults (all flags `false`, matching current Dockerfile defaults).
- Concurrent builds are blocked — the save is disabled while `Building` — so the live flags are never ambiguous.
