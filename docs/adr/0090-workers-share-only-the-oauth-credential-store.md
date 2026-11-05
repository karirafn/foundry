# Workers Share Only the OAuth Credential Store

## Context

In OAuth mode every worker mounted the shared, writable `foundry-claude-credentials` volume as its Claude config dir (`CLAUDE_CONFIG_DIR` → `/home/node/.claude`). Besides `.credentials.json`, Claude Code loads `CLAUDE.md`, `skills/`, `agents/`, `commands/`, `settings.json` and `.claude.json` from that directory. Any worker could therefore leave instructions or configuration behind that every concurrent and later worker would load. [ADR 0086](0086-worker-policy-in-root-rendered-managed-settings.md) records this as the remaining cross-worker path.

The workers still have to share the OAuth credentials. The CLI rotates the refresh token on each refresh, and a worker holding a stale copy would lose authentication.

## Decision

The worker mounts the credential volume at a dedicated path, `/home/node/.claude-credentials`, and sets `CLAUDE_SECURESTORAGE_CONFIG_DIR` to that path. `CLAUDE_CONFIG_DIR` is not set, so the CLI uses its default `~/.claude`. That directory is on the worker's anonymous `/home/node` volume ([ADR 0088](0088-worker-containers-run-hardened-by-default.md)) and is removed with the container. API-key mode is unchanged: no volume and no config-dir variables.

The pinned CLI (2.1.316) resolves its credential store, `.credentials.json` and the `.storage-write` refresh lock, from `CLAUDE_SECURESTORAGE_CONFIG_DIR`, and falls back to `CLAUDE_CONFIG_DIR` only when the variable is unset. A spike (recorded on issue #679) forced a real token refresh through a TLS-intercepting proxy and found:

- The refreshed `.credentials.json` landed on the volume, and the volume held nothing else after the run.
- `.claude.json`, `projects/`, `sessions/` and `plugins/` were all written to the container-local config dir.
- A fresh per-container `.claude.json` did not force a re-login: the CLI re-fetched the profile with the shared token.

The worker entrypoint resolves the credential file the way the CLI does: `CLAUDE_SECURESTORAGE_CONFIG_DIR` first, then `CLAUDE_CONFIG_DIR`. The base image pre-creates `~/.claude-credentials` owned by `node`, so the mountpoint is writable whichever container mounts the volume first.

## Considered Options

- **Symlink `~/.claude/.credentials.json` into a separately mounted credential volume**: rejected. The CLI persists through an atomic temp-file + `rename`, which replaces the symlink with a regular file, so the refreshed token stays in the container. Some CLI read paths also open the file with `O_NOFOLLOW` and treat a symlink as `refused-symlink`.
- **Single-file mount (`volume-subpath=.credentials.json`) over `~/.claude/.credentials.json`**: rejected, though it works. The rename fails with `EBUSY` and the CLI falls back to an in-place write. It needs Docker Engine ≥ 26 / API 1.45, and container creation fails if the file is missing from the volume. The refresh lock also stays per-container, so concurrent refreshes from different workers would no longer be serialised.
- **`allowManagedHooksOnly` in managed settings**: rejected. It covers hooks only, and `CLAUDE.md`, skills, agents and `env` stay shared.

## Consequences

- Nothing a worker writes to its Claude config dir reaches another worker. The worker can still write arbitrary files into the credential volume, but no worker loads that directory as a config dir.
- The login, credential-helper, Credit Probe and usage containers still mount the volume as their config dir (`/home/node/.claude`). Files a worker plants in the volume are therefore still visible to those Foundry-run containers. Moving them to the same split is follow-up work.
- `CLAUDE_SECURESTORAGE_CONFIG_DIR` is not a documented CLI setting. Before a CLI bump is merged, check that the credential store still follows it. The `entrypoint.bats` and `IssueClaimedHandler` tests pin Foundry's side of the wiring.
- Two workers refreshing at once still serialise on the shared `.storage-write` lock, as before.
