---
reaches: external-contract
---

# One Account Token, Verified Against Every Permission Foundry Uses

## Context

[ADR 0084](0084-branch-rules-written-by-a-separate-server-side-token-on-operator-action.md) put branch-rule writes (and later webhook registration) on a second, optional branch-rules token, because the account token was injected into every worker container.
[ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md) removed every provider credential from workers.
The account token is now used only by Foundry's server code and by the model-free Publisher, and every comment, pull request, merge and label write is code.
On GitLab the split already carried no separation, because merging ([ADR 0105](0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)) needs the Maintainer role the branch-rules token required.
This replaces ADR 0084's separate-token clause.

## Decision

Each account holds one token, and that token must hold every permission Foundry uses it for.
Foundry verifies this rather than assuming it:

- **One permission list per provider** declares each permission's display label, template-URL parameter and how it is verified.
  The account form's list, the token creation link and the write-probe sequence are all derived from it.
- **GitHub tokens must be fine-grained** (`github_pat_` prefix).
  Any other token fails validation, and a saved one makes every repository of its account ineligible until the token is replaced.
- **GitHub fine-grained** permissions are probed per repository with deliberately invalid requests.
  Contents, Issues, Pull requests, Administration (`POST /rulesets`) and Webhooks (`POST /hooks`) read 422 as granted and 403 as missing.
  Actions (write) is probed with `POST /actions/jobs/1/rerun`, a job id that does not exist: 404 is granted, 403 is missing.
  Commit statuses is probed with a GET that reads 200 as granted and 403 as missing.
  Any other response is unknown and fails closed.
  Workflows (write) cannot be verified before use.
- **GitHub cannot report a token's permissions**, so extra permissions and "All repositories" access are not checked.
  The account form says so, and the creation link preselects exactly the list.
- **GitLab tokens must be fine-grained**, on GitLab 19.5 or later, where `GET /personal_access_tokens/self` reports the token's `granular_scopes`.
  Foundry checks them exactly: each project entry carries exactly the project permission set, user entries exactly `read_personal_access_token` and `read_user`, and a group entry nothing but `read_package` (allowed for a package feed, never required).
  An entry reaching all memberships, personal projects or the instance is rejected, as is any missing or extra permission.
  A repository is covered when a project entry carries its project id, and its token user still needs the Maintainer role (access level 40).
  Legacy, group and project access tokens are rejected.
- **A probe verdict records the permission list it was taken against.**
  A verdict taken against an older list counts as unprobed and is re-probed automatically, so adding a permission to the list reaches every repository with no migration.
- **A Denied verdict is re-probed after a one-hour cooldown**, because a token edited in place on the provider (same token string) raises no credential event.
  Manual re-check stays the immediate path.

A repository whose token lacks any verifiable permission is ineligible.
Its account row names the missing permissions with the remedy for its provider.

## Considered Options

- **Keep both tokens and restate the reason** — rejected for operator friction.
  The remaining separation was that a compromise of the Publisher, which reads an agent-written bundle while holding the token, could not delete the repository or remove default-branch protection.
- **Use the branch-rules token when set, else the account token** — rejected: it makes the less-separated setup the easy path while keeping two code paths.
- **Discover missing permissions by attempting the write** (ADR 0084's rule for the branch-rules token) — rejected: a token must not be accepted for a use it cannot perform.
- **A one-off migration resetting every verdict** — rejected for a recorded permission-list version, which needs no migration the next time the list changes.
- **Authenticate as a GitHub App minting per-purpose tokens** — still deferred, as in ADR 0084.
  It would restore separation without the operator cost, but is a large prerequisite.
- **Accept legacy GitLab tokens on hosts older than 19.5** — rejected: a second, unverified path holding the broadest token.
- **Let group entries carry the project permission set** — rejected: coverage would need group-ancestry lookups and more permissions.
- **Probe GitHub tokens for extra permissions** — rejected: a denylist reads as complete while every permission off it passes, and probing all of GitHub's ~30 repository permissions costs ~30 requests per repository per check and still misses account permissions.

## Consequences

- The Publisher holds a token with Administration and Webhooks write.
  An exploit of git's bundle or pack handling there could delete the repository, rewrite the default branch or re-point webhooks.
  A token with Contents and Pull requests write alone could already land code on the default branch through a pull request Foundry merges, so the added exposure is destructive power, not code integrity.
- After the upgrade, every repository whose account token lacks Administration or Webhooks becomes ineligible until the token is edited or replaced.
- A Denied repository costs at most six write-probe POSTs per hour against GitHub's secondary rate limit.
- The probe classification rests on a spike run on 2026-11-18 against `karirafn/foundry` with two fine-grained tokens.
  It returned 403 for a missing Administration, Webhooks or Contents permission and 422 when granted.
  The missing case for the read GETs was not observed, which is why their unknown responses fail closed.
- The Actions write probe rests on a spike run on 2026-11-24 against `karirafn/foundry` with two fine-grained tokens: `POST /actions/jobs/1/rerun` returned 404 with Actions write and 403 ("Resource not accessible by personal access token") without it, with or without a malformed body.
- Every new GitLab project must be added to its account's token by hand.
- The GitLab rules rest on a spike run on 2026-11-24 against gitlab.com: `granular_scopes[].permissions` reports the names chosen at creation (`read_job`, `run_job`), and `/personal_access_tokens/self`, `/user` and `/projects` each answered 403 `insufficient_granular_scope` without their user-level permission.

## Revisions

- 2026-11-24 — GitHub account tokens must be fine-grained; Actions needs write, verified by a rerun probe (#1287)
- 2026-11-24 — GitLab account tokens must be fine-grained and hold exactly the listed permissions on selected projects (#1288)
