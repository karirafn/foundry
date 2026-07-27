# Classify Only 403 as Missing; 422 and 404 as Granted; 5xx/Transport as Indeterminate

## Context

`POST /repos/{owner}/{repo}/git/refs` with an all-zeros SHA is used as a non-destructive write
probe. The response status code must be classified as either permission-granted, permission-missing,
or indeterminate, and each classification drives a different caller outcome.

## Decision

**`422` (Unprocessable Entity) → Granted.**
GitHub returns `422` with `"message": "Object does not exist"` when the SHA is not resolvable
but the token has Contents write permission. This is the expected non-destructive success path:
the ref cannot be created because the SHA is invalid, not because the token lacks permission.

**`404` (Not Found) → Granted.**
A `404` means GitHub could not find the repository (e.g., a just-deleted repo) or the endpoint
path is unexpected. In either case the probe cannot confirm denial, so the caller continues;
subsequent operations will surface the real error.

**`403` (Forbidden) → Missing.**
A `403` is the only status code that conclusively indicates the token lacks write permission.
It may also indicate a pending org approval (SSO SAML enforcement) — the caller must surface
both possible causes in any error message shown to the user.

**`5xx` / transport error → `Result.Fail`.**
Server errors and network failures are indeterminate — they do not confirm or deny permission.
Returning `Result.Fail` prevents false-positive or false-negative permission classifications.

## Consequences

- A `403` from org SAML enforcement is indistinguishable from a `403` from missing permission;
  block messages must name both causes.
- `2xx` responses from the probe endpoint are impossible when the payload is invalid (all-zeros
  SHA), so no production path reaches a success arm for `2xx`.
