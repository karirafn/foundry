---
status: accepted
---

# Persisted LastImageBuiltAt as the Usable-Image Signal

## Context

The frontend needs a read-path signal to determine whether a usable worker image has ever been built, so it can show a full-screen blocking overlay during the initial cold build. The question is how to derive that signal reliably on every `GET /api/settings` request.

## Decision

Persist a `LastImageBuiltAt` (`DateTimeOffset?`) column on `GlobalSettings`. `CompleteImageBuild()` stamps it with the current UTC time alongside setting `ImageBuildState = Idle`. It is `null` on a freshly created aggregate (never built) and is never cleared by `BeginImageBuild()` or `FailImageBuild()`, so a prior successful build is not erased by a subsequent failure. `GlobalSettingsSummary` exposes a derived `HasUsableImage` boolean (`LastImageBuiltAt is not null`) that the frontend consumes directly without additional inference logic.

Existing installs are backfilled by the migration: rows whose `image_build_status` JSON is `{"type":"idle"}` receive `last_image_built_at = datetime('now')`, so healthy installs already past the cold build are not blocked by the overlay after upgrade.

## Considered Options

- **Query Docker on every read** — ask the Docker daemon whether the `foundry-worker` image exists as a proxy for "is there a usable image". Rejected: it couples every settings read to a Docker API call, adds latency and a new failure mode to a hot read path, and still returns stale data if the image was pruned externally. It also requires the API to hold a Docker socket reference in the read path, not just the build path.

## Consequences

- `LastImageBuiltAt` is a persistence-level marker, not a Docker-existence guarantee. If the image is pruned out-of-band (e.g. `docker system prune`), `HasUsableImage` remains `true` until the next build cycle, which sets it back to `false` (via a `FailImageBuild`) or confirms it (via `CompleteImageBuild`). This is acceptable because the dispatch gate in `WorkerDispatchService` performs a live Docker-existence check before each dispatch; the overlay's false-positive case is benign compared to the alternative latency cost.
- Docker-existence verification on the read path remains the documented upgrade path if the overlay needs tighter accuracy in a future iteration.
