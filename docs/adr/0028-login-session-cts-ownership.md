# Per-Flow CancellationTokenSource Ownership in the Login Session

## Context

`LoginSessionService` runs two concurrent flows over shared state.
The fire-and-forget background task `RunSessionAsync` starts the login container, scans for the OAuth URL, and then waits (polling every 100ms) for either the operator to submit a code or the session to time out.
The foreground `SubmitCodeAsync` (invoked from the HTTP endpoint) transitions the session to `SigningIn`, delivers the code via `docker exec`, and then scans the logs for the login result.

Both flows shared a single `CancellationTokenSource` (`_sessionTimeoutCts`) and the `_activeSession` field.
Submitting a code transitioned the phase to `SigningIn`, which caused the background poll to return and its `finally` to **dispose `_sessionTimeoutCts`** (and null `_activeSession`).
`SubmitCodeAsync`, still awaiting the slow `docker exec` code delivery (>100ms), then read `_sessionTimeoutCts.Token` on the now-disposed source and threw `ObjectDisposedException` — surfacing as `Unknown` ("Sign in failed") on every real login.
Unit tests missed it because the fake orchestrator's code delivery completed synchronously, so `SubmitCodeAsync` read the token before the background poll disposed it — the race window only exists when delivery is slow (real Docker).

## Decision

Each flow owns its own `CancellationTokenSource`; ownership is never shared across the background → foreground handoff.

- `_sessionTimeoutCts` is owned exclusively by the background `RunSessionAsync` and disposed unconditionally in its `finally`. `SubmitCodeAsync` no longer reads it.
- `SubmitCodeAsync` creates its own `signInCts` for the post-code log scan, bounded by `LoginSessionOptions.SignInTimeout` and linked to the host token so shutdown propagates. A sign-in-scan timeout maps to `LoginFailureReason.CodeTimeout`; host-shutdown cancellation exits cleanly without broadcasting a failure.
- `_activeSession` is cleared through a single identity-guarded helper `ClearActiveSession(session)` (clears only when `_activeSession` still refers to the same session). The background `finally` clears only in pre-handoff phases (`Starting`/`WaitingForAuthorization`); once the phase is `SigningIn`, `SubmitCodeAsync` owns clearing. Every terminal path (URL timeout, code timeout, invalid code, success, committed-then-threw, unexpected error, host shutdown) clears exactly once.
- The fire-and-forget `Task.Run` body is wrapped in try/catch-log so a host-cancellation `OperationCanceledException` (which escapes both inner catch filters) can never fault the task unobserved.

## Considered Options

### `handedOff` flag guarding the shared CTS disposal

The background task could set a flag when the code is submitted and skip disposing the shared `_sessionTimeoutCts` so `SubmitCodeAsync` could keep using it.
Rejected: it keeps the two flows coupled through one disposable and requires choreographing the flag correctly across threads — one more thing to get subtly wrong.
Decoupling (each flow owns its own CTS) is correct by construction with no handshake to maintain.

## Consequences

- Reintroducing a shared `_sessionTimeoutCts` read from `SubmitCodeAsync` would reintroduce the race; the ownership rule is deliberate and load-bearing.
- The post-code scan is bounded by its own `SignInTimeout` (distinct from the pre-code `SessionTimeout`), so a stuck token exchange fails as `CodeTimeout` rather than hanging.
- A fake dependency that completes synchronously can hide latency-dependent concurrency bugs; the login fake gained a gated (arm/release) code-delivery so the dispose-race is reproduced deterministically in a unit test.
