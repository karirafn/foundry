---
reaches: non-functional
---

# Branch Rules Written Server-Side on Operator Action

## Context

A newly added repository is `Ineligible` until someone configures default-branch protection on the provider ([ADR 0013](0013-repository-level-eligibility.md)), so Foundry gains the ability to write those rules itself.
Writing them needs more privilege than any worker should hold: a GitHub repository ruleset needs the fine-grained **Administration: write** permission, which also grants `DELETE /repos/{owner}/{repo}` and branch-protection removal, and a GitLab protected branch needs the Maintainer role.
No worker container holds a provider credential ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)), so that power must stay with Foundry's server-side code and never reach an autonomous worker that could delete the very rules that gate its dispatch.

## Decision

Branch rules are written only on an explicit operator action: adding a repository with the opt-in left checked, the repository card's apply action, or replacing an account's token, which applies to that account's opted-in repositories.
The poll cycle stays read-only toward the provider's branch rules, so a deliberate relaxation of protection made on the provider is reported as `Ineligible`, never silently reverted.

The write uses the account token, read only by server-side code and never injected into worker containers; that token must hold the permissions the write needs ([ADR 0115](0115-one-account-token-verified-against-every-permission-foundry-uses.md)).

Foundry writes only what eligibility checks, and only additively: on GitHub a single ruleset named `foundry-eligibility` on `~DEFAULT_BRANCH` (`pull_request` with zero required approvals, `non_fast_forward`, `deletion`, no bypass actors), upserted by name; on GitLab it modifies the existing default-branch protection in place, or creates one if none exists, and never unprotects the branch.
The outcome of each write is confirmed by re-running the branch-rules GET, never inferred from the write's success status.

The same server-side token also registers provider webhooks ([Webhook Registration](../../DOMAIN.md#webhook-registration), [ADR 0085](0085-webhooks-nudge-the-poll-loop.md)), which needs GitHub's fine-grained **Webhooks: read and write** permission (GitLab's Maintainer role covers both). Webhook write power lets the holder re-point or create hooks and so read every repository event, so it must not reach worker containers either. Unlike branch rules, webhook registration also runs without a per-repository operator action — on repository add, a public-base-URL change, a hook turning `Silent`, and the scheduled poll's stale-stamp step. These writes touch only the hooks Foundry owns; the poll loop still never writes or repairs branch rules.

## Considered Options

- **Repair rules on every poll** — rejected: it overrides deliberate human relaxations and turns the poll into a provider write against the request budget ([ADR 0066](0066-provider-api-request-budget-governor.md)).
- **Elevate the account token while it was still injected into workers** — rejected: workers would inherit repository-delete and rule-removal power, defeating the guarantee the rules exist to provide.
- **A separate, optional branch-rules token, read only server-side** — the original decision, replaced by [ADR 0115](0115-one-account-token-verified-against-every-permission-foundry-uses.md) once workers held no credential and the split carried no remaining separation worth its operator cost.
- **A third, webhook-only token** — rejected as more operator ceremony for no added separation, since both uses are server-side.
- **Authenticate as a GitHub App minting per-purpose tokens** — deferred: the cleanest separation, but a large prerequisite outside this change.
- **GitLab unprotect-then-reprotect** — rejected: it fails open, leaving the default branch unprotected between the two calls or permanently if the second fails.

## Consequences

- "Newly added repositories are eligible" holds when the operator leaves the opt-in checked and the account token holds the permissions the write needs.
- GitHub's branch-rules GET reports ruleset rules only (classic protection on `karirafn/foundry` did not appear in it), so a classic-protected repository receives the Foundry ruleset alongside its classic protection — additive, nothing loosened.

## Revisions
- 2026-11-04 — The token gained a second consumer, webhook registration, which also runs on repository add, a public-base-URL change and a hook turning `Silent` ([ADR 0085](0085-webhooks-nudge-the-poll-loop.md))
- 2026-11-18 — The separate branch-rules token is replaced by the account token; the Context claim that the account token reached every worker had drifted since workers stopped holding a provider credential ([ADR 0115](0115-one-account-token-verified-against-every-permission-foundry-uses.md))
