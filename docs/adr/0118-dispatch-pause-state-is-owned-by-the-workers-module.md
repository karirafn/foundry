---
reaches: module-contract
---

# Dispatch Pause State Is Owned by the Workers Module

## Context

Every dispatch-pause reason lives on the Settings module's `GlobalSettings` row: the operator's manual pause (`IsDispatchPaused`), the usage-limit pause (`UsageLimitResetsAt`) and the per-host pauses (`HostPauses`).
Most of the transitions are made outside Settings: Workers calls `PauseHost`, `RecordProbeUnreachable`, `ClearHostPause`, `ClearUsageLimitPause` and `SetUsageLimitResetsAt` on the aggregate and saves it, and Credentials calls `SetUsageLimitResetsAt` from its credit probe.
Those are the only reasons Workers and Credentials reference the Settings implementation project rather than only `Settings.Contracts`, and the pause vocabulary (`DispatchPaused`, `DispatchResumed`) already lives in `Workers.Contracts`.
Two invariants rest on the current placement: every pause reason sits on one row, so "Resume All" clears them in one method; and a host pause is staged unsaved so it commits in the same save as the run failure that found the host unreachable (`HostPauseStaging`).

## Decision

The runtime dispatch-pause state lives in a single-row aggregate owned by the Workers module.

- **What it holds** — `IsDispatchPaused`, `UsageLimitResetsAt`, `HostPauses`, the package-proxy pause, and every other runtime pause reason. They are exactly what "Resume All" clears.
- **What stays in Settings** — `AutoResumeOnUsageReset` is an operator preference, edited by `PUT /api/settings/dispatch` together with the probe and poll intervals; Workers reads it through `IGlobalSettingsQueries`.
- **Endpoints** — `POST /api/settings/dispatch/pause` and `/dispatch/resume` are registered by Workers on their existing routes; `GET /api/settings` keeps its pause fields, filled by Settings from a `Workers.Contracts` query. The HTTP contract does not change.
- **Credentials** — the credit probe publishes its own integration event when it finds a usage limit, and a Workers handler applies it through the outbox.
- **One source of truth** — the pause state exists only on the Workers aggregate, never also on `GlobalSettings`. A Workers seeder creates the row on a fresh database with a fixed id, as `SettingsSeeder` does.

The one-row invariant and the same-save host-pause staging both hold, because the state and its main writer now live in one module.

## Considered Options

- **Keep the state on `GlobalSettings`, and have Workers and Credentials raise events that Settings applies** — the host pause would commit up to one outbox tick (2 s) after the run failure, letting another run dispatch to an unreachable host; callers that branch on a transition's result would move into Settings handlers.
- **Accept the shared write as a documented exception** — keeps three writing modules on one aggregate, and makes each new pause reason a further cross-module writer.
- **Expand/contract with dual writes** — two tables that can disagree about whether dispatch is paused, and a "Resume All" that must clear both.

## Consequences

- Workers and Credentials drop their project references to `Foundry.Modules.Settings`.
