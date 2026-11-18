# Webhook Ingress Is Required and Verified by a Self-Probe

## Context

[ADR 0085](0085-webhooks-nudge-the-poll-loop.md) made webhook ingress optional: without `Webhooks:Port` or a Webhook Public Base URL every repository stayed `NotConfigured` and polled at the normal interval.
The URL was checked for shape only (absolute `https`, no query, fragment or userinfo), so a URL routed to another machine, a stopped tunnel or `localhost` saved cleanly and failed only as hooks that never delivered.
Hooks registered before a newly subscribed event (`push`) kept their old event list until an operator re-ticked it.
This replaces ADR 0085's Ingress clause that left ingress to the operator's discretion; its nudge model and health-gated backstop stand.

## Decision

Webhook ingress is required, and Foundry verifies that its public URL reaches this process before accepting it.

- **Always-bound port** — the webhook-only port is bound on every start, defaulting to 5099; configuration overrides it, and an occupied port fails startup.
- **Required URL** — the first-run wizard asks for the Webhook Public Base URL before repositories are added, and the setup guard returns to the wizard while no URL is stored.
  The URL can be changed but not cleared.
- **Literal host rules** — `localhost` and literal loopback, link-local and unspecified addresses are rejected; private ranges are allowed for self-managed providers on a LAN.
  The host is never resolved, because resolution from Foundry's vantage differs from the provider's.
- **Self-probe on change** — a URL that differs from the stored one is accepted only when a one-time code, generated and held in memory by this process, arrives through `<base>/webhooks/probe/<code>` on the webhook port with a 2xx answer and normal TLS validation.
  An unknown code answers 404, like an unknown hook id.
- **Per-repository confirmation** — after each successful registration Foundry triggers the provider's test delivery (GitLab's hook test, GitHub's ping), so a hook becomes `Active` without waiting for organic traffic and the provider's own reachability is confirmed.
- **Reconciled hooks** — each repository records a stamp of the hook spec it was registered with (the provider's subscribed events and flags plus the base URL).
  A scheduled poll registers any repository whose stamp is stale, whose token is readable and whose write-probe verdict is not `Denied`, at most once per hour after a failure; the startup catch-up poll therefore reconciles every hook after an upgrade.
- **Hook identity by hook id** — a registration matches every provider hook whose URL ends in the repository's `/webhooks/{provider}/{hookId}`, updates the first and deletes the rest; removing a repository deletes its hooks without blocking the removal.

Polling remains the health-gated backstop of ADR 0085; no repository is made ineligible by its hook state.

## Considered Options

- **Gate eligibility on an `Active` hook** — rejected: [ADR 0115](0115-one-account-token-verified-against-every-permission-foundry-uses.md) already makes a token without the Webhooks permission ineligible, and a transient registration failure would block dispatch with no exit faster than the next trigger.
- **Echo a stored instance id from the probe endpoint** — rejected: a copied install passes, and the endpoint discloses the id to anyone.
- **Reject private and resolved-private hosts** — rejected: it breaks LAN-hosted GitLab and judges reachability from Foundry's DNS view rather than the provider's.
- **Delete old hooks and create new ones on a URL change** — rejected for in-place update: deletion leaves a window with no hook and discards delivery history.
- **Upsert every hook on every start, or read each hook back and diff it** — rejected for the stamp: both spend provider budget ([ADR 0066](0066-provider-api-request-budget-governor.md)) on every restart when nothing changed.

## Consequences

- A fresh install cannot finish setup without a tunnel or reverse proxy that reaches the webhook port.
- The webhook port gains a public, unauthenticated probe path; it reveals only whether a code is pending in this process.
- A stored URL that later stops routing is not re-probed on unrelated saves; `Silent` and normal polling cover it as before.
- A hook edited by hand on the provider is not detected by the stamp; the missed-change transition to `Silent` re-registers it.
