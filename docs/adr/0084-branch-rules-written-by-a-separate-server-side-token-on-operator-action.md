# Branch Rules Written by a Separate Server-Side Token on Operator Action

## Context

A newly added repository is `Ineligible` until someone configures default-branch protection on the provider ([ADR 0013](0013-repository-level-eligibility.md)), so Foundry gains the ability to write those rules itself.
Writing them needs more privilege than any worker should hold: a GitHub repository ruleset needs the fine-grained **Administration: write** permission, which also grants `DELETE /repos/{owner}/{repo}` and branch-protection removal, and a GitLab protected branch needs the Maintainer role.
The account token is injected into every worker container as `GH_TOKEN` / `GITLAB_TOKEN` (`IssueClaimedHandler`), so elevating it would hand each autonomous worker the power to delete the very rules that gate its dispatch.

## Decision

Branch rules are written only on an explicit operator action: adding a repository with the opt-in left checked, the repository card's apply action, or saving a branch-rules token, which applies to that account's opted-in repositories.
The poll cycle stays read-only toward the provider, so a deliberate relaxation of protection made on the provider is reported as `Ineligible`, never silently reverted.

The write uses a separate, optional branch-rules token stored on the account, read only by the server-side enforcer and never injected into worker containers.
Without one, enforcement reports that it was skipped and the repository behaves as before.

Foundry writes only what eligibility checks, and only additively: on GitHub a single ruleset named `foundry-eligibility` on `~DEFAULT_BRANCH` (`pull_request` with zero required approvals, `non_fast_forward`, `deletion`, no bypass actors), upserted by name; on GitLab it modifies the existing default-branch protection in place, or creates one if none exists, and never unprotects the branch.
The outcome of each write is confirmed by re-running the branch-rules GET, never inferred from the write's success status.

## Considered Options

- **Repair rules on every poll** — rejected: it overrides deliberate human relaxations and turns the poll into a provider write against the request budget ([ADR 0066](0066-provider-api-request-budget-governor.md)).
- **Elevate the existing account token** — rejected: workers inherit repository-delete and rule-removal power, defeating the guarantee the rules exist to provide.
- **Authenticate as a GitHub App minting per-purpose tokens** — deferred: the cleanest separation, but a large prerequisite outside this change.
- **GitLab unprotect-then-reprotect** — rejected: it fails open, leaving the default branch unprotected between the two calls or permanently if the second fails.

## Consequences

- "Newly added repositories are eligible" holds only for accounts with a branch-rules token; without one, adding a repository behaves exactly as before.
- GitHub's branch-rules GET reports ruleset rules only (classic protection on `karirafn/foundry` did not appear in it), so a classic-protected repository receives the Foundry ruleset alongside its classic protection — additive, nothing loosened.
