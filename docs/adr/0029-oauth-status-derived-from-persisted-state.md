# Derive OAuth Status from Persisted State, Not a Credential-Volume Read

## Context

The Settings panel shows an OAuth status of `NotConfigured`, `Present` ("Signed in"), or `ReLoginNeeded`.
The original `CredentialVolumeReader` computed this by reading `.credentials.json` from the credential volume via **host filesystem I/O** on the daemon-reported mountpoint (`/var/lib/docker/volumes/…`).

On Docker Desktop that mountpoint lives inside the Linux VM and is unreachable from the host process, so the read always returned "not present" → `ReLoginNeeded`, even immediately after a verified successful login (the credential provably was on the volume).
The check was also inconsistent: the `IGlobalSettingsQueries` path already passed `null` for the volume status, so it always reported `ReLoginNeeded` regardless.

## Decision

Compute OAuth status from persisted database state; do not read the credential volume for status display.

`GlobalSettingsMapper.ComputeOAuthStatus`:

- not OAuth mode → `NotConfigured`
- auth-invalid pause set → `ReLoginNeeded`
- a committed account identity present (`GlobalSettings.OAuthAccountEmail`, set by a successful in-app login) → `Present`
- otherwise → `ReLoginNeeded`

`CredentialVolumeReader`, `ICredentialVolumeReader`, and `CredentialVolumeStatus` are removed; `GetSettings` no longer reads the volume.
Both settings-read paths now use the same DB-derived logic, resolving the prior inconsistency.

The authoritative signals already live in the database: a committed identity means login succeeded, and the auth-invalid pause is set when a worker actually observes an auth failure at runtime — the real "the credential no longer works" event.
Volume-file *presence* is a weak proxy that cannot even detect an expired-but-present token (the file remains after expiry; the CLI refreshes in place), so reading it adds fragility without meaningful correctness.

## Considered Options

### Read the volume via a helper container, with a TTL cache

Read `.credentials.json` inside a short-lived helper container (cross-platform, like [ADR 0027](0027-credential-volume-read-via-helper-container.md)), plus a TTL cache + single-flight + event-driven invalidation so frequent `/api/settings` reads do not each spin a container.
Rejected for now: it reflects live volume *presence* but still cannot judge validity, so it buys little over the DB-derived signals while adding a container spin per refresh, a caching layer, and its invalidation wiring.
The one case it handles that the DB-derived approach does not — a credential seeded on the volume out-of-band with no in-app login committed — is not a supported path now that in-app login works.

## Consequences

- Status is fast, cross-platform, and needs no Docker access for display.
- Token expiry is deliberately not surfaced: the CLI auto-refreshes the access token in place on the volume, so any value captured at login-commit would be stale within hours and misleading. The "Token expires" UI row and the contract's `ExpiresAt` field were removed rather than populated.
- A credential seeded out-of-band (no in-app login) reads as `ReLoginNeeded` until an in-app login commits an identity. Accepted: in-app login is the intended path.
- `ComputeOAuthStatus` intentionally ignores token expiry entirely; the credential auto-refreshes on the volume, and the authoritative re-login trigger is the auth-invalid pause raised by a real worker auth failure.
