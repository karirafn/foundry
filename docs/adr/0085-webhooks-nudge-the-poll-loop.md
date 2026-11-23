---
reaches: structure
---

# Webhooks Nudge the Poll Loop; Polling Becomes a Health-Gated Backstop

## Context

Foundry discovers every provider change by polling (`MonitoringService` every 30 s, `WorkerDispatchService` every 10 s), which spends provider request budget in proportion to time rather than change, and delays every change by up to one poll interval. GitHub and GitLab webhooks can signal those changes as they happen, but deliveries can be lost, arrive out of order, carry no aggregate state (GitHub's check rollup), and do not exist for every polled concept (GitLab issue links and protected branches have no webhook event). The WebApi is unauthenticated, so any public ingress must expose only the webhook surface.

## Decision

A verified webhook delivery is a **nudge**: a request to run one existing poll pass for `(repository, pass, issue?)` now. It never applies payload state to the domain. `RepositoryPoller` stays the single place that fetches provider state, diffs it, and raises integration events, building on the single-loop concurrency control of [ADR 0005](0005-background-service-polling-dispatch.md).

- **Ingress** — a dedicated Kestrel port, always bound, serves only `/webhooks/*`; the main port refuses `/webhooks/*`. The operator supplies the tunnel or reverse proxy; Foundry stores the public base URL in Global Settings. Ingress is required: setup requires the public base URL and verifies it with a self-probe ([ADR 0116](0116-webhook-ingress-is-required-and-verified-by-a-self-probe.md)).
- **Verification before parsing** — each repository has its own generated secret, encrypted at rest, addressed by an opaque hook id in the URL. The secret is checked (GitHub `X-Hub-Signature-256` HMAC, GitLab `X-Gitlab-Token` constant-time compare) before the body is read, and the body's repository must match the hook id.
- **Single consumer** — the endpoint enqueues into a bounded in-memory channel and returns `202`. `MonitoringService` is the channel's only consumer: it wakes immediately, coalesces a ~2 s window, and runs targeted passes in the same loop as scheduled polls. Lost nudges are recovered by the backstop.
- **Budget bound** — events that touch nothing Foundry tracks are dropped without a provider call, and each event-triggered nudge key runs at most once per normal poll interval (leading run, trailing coalesced run). Webhook mode therefore never exceeds the call rate of normal polling, extending the budget ownership of [ADR 0066](0066-provider-api-request-budget-governor.md) to event-triggered calls. Deadline nudges run at their deadline, outside the per-key cap, and do not stamp the cap window; each deadline producer bounds its own re-arming (the rebase landing re-check by `PendingRebase.LandingTimeout`, its backoff by `MaxTransientFailures`, two checks deadlines per episode, one follow-up per batch or held comment set), and the provider rate budget still caps total calls underneath.
- **Health-gated backstop** — a repository polls at the 15-minute Backstop Interval only while its Webhook Status is `Active`; `NotConfigured` (only until setup stores a URL), `NotRegistered`, `Pending` and `Silent` poll at the normal interval. A hook starts `Pending` and becomes `Active` on its first verified delivery (including `ping`). A backstop poll that finds a **missed change** — one the provider has a webhook event for, yet no delivery announced — moves the hook to `Silent`; the next verified delivery restores `Active`. Changes with no webhook equivalent on that provider never count as missed.
- **Deadline nudges** — time-based exits that no provider event can trigger (the checks-gate grace window and upper bound of [ADR 0077](0077-gate-review-on-provider-checks-at-the-issue-level.md)) schedule not-before nudges, so a CI-less repository leaves `ChecksPending` on time under the backstop. Likewise, a review pass that finds comments held back only by the quiet period of [ADR 0070](0070-any-human-comment-is-actionable-feedback.md) schedules a not-before `Review` nudge for when the newest of them becomes actionable, so a nudged PR comment is not left to the backstop.

## Considered Options

- **Apply payloads directly** — lowest traffic, rejected: it forks change detection into a second path that must re-implement the feedback cutoff, label-scope classification and complete-listing untrack guard, handle out-of-order delivery, and still cannot compute GitHub's check rollup, while the backstop poll remains anyway.
- **Hybrid (apply cheap events, nudge the rest)** — rejected for the same second-path drift at smaller scale.
- **Expose the existing WebApi publicly** — rejected while the API is unauthenticated; it makes public exposure the default.
- **Process the delivery inside the HTTP request** — rejected: concurrent deliveries race the same pass and a slow provider call risks the provider's delivery timeout.
- **Persist nudges in an outbox-style table** — rejected: the backstop already covers lost nudges.
- **Gate health by querying the provider's hook status** — deferred: costs a call per repository and misses a down tunnel; missed-change detection costs nothing.
- **Conditional requests (ETag / 304)** — reduces GitHub REST cost but delivers no latency gain and does not apply to the GraphQL issue listing.

## Consequences

- Change latency drops from up to one poll interval to roughly the coalescing window for every event-mapped change.
- Changes with no webhook equivalent (GitLab issue links and protected branches, cross-repository blockers) are reflected within the Backstop Interval rather than the normal poll interval while a hook is `Active`.
- A broken hook costs at most one Backstop Interval of latency, once, before `Silent` restores normal polling.
- `RepositoryPoller` gains per-pass entry points; the call-invariance test gains a nudge-cap assertion.

## Revisions
- 2026-11-09 — The per-key nudge cap governs event-triggered nudges only; deadline nudges run at their deadline, correcting the claim that a CI-less repository left `ChecksPending` on time, which a recent `Checks` run could defer by a full interval (#796)
- 2026-11-18 — Webhook ingress is required rather than optional: the webhook port is always bound and the public base URL is required and verified by a self-probe ([ADR 0116](0116-webhook-ingress-is-required-and-verified-by-a-self-probe.md))
