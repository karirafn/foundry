# Generic Secret Patterns Are Published for Review

## Context

The Diff gate ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)) blocked the push on every gitleaks finding.
Every block whose findings could be verified was a `generic-api-key` false positive — five of five as of 2026-11-14: a versioned storage key, two test-fixture UUIDs, an ASP.NET Identity hash of a documented temporary password in a README, and a common password under test in a password-blocklist feature.
Each block cost an operator decision, and two rounds of allowlist tuning (#901) and a confirmation action (#930) had not stopped new false-positive classes arriving.
Meanwhile the one credential an OAuth worker can read, the Claude OAuth access and refresh token, matched no rule in gitleaks 8.30.1's default set.
This decision replaces 0100's **Diff gate** clause "a detected secret blocks the push" for generic patterns only.

## Decision

Gitleaks rules split into two classes, marked in `workers/publisher/gitleaks.toml`, the single source of a rule's class:

- **Provider pattern** — a rule for one service's token format (`github-pat`, `gitlab-pat`, `aws-access-token`, `anthropic-api-key`, Foundry's own `anthropic-oauth-token`). A match blocks the push exactly as before, and an operator confirms or discards it.
- **Generic pattern** — a rule for a free-form secret shape, tagged `foundry-generic` (today only `generic-api-key`). A match is a **generic finding**: the Publisher pushes, reports it as `FOUNDRY_GENERIC_FINDING`, and the pull request lists its path, line and rule under "Possible generic secrets for review". A generic finding never blocks and never enters the confirmed set.

The terms and the split follow GitHub secret scanning, whose push protection covers provider patterns and does not cover passwords (docs.github.com, "Supported secret scanning patterns").

## Considered Options

**Add an allowlist per false-positive class:** rejected — the #901 approach; each new class costs an issue and a blocked run before it is known, and the classes seen so far (fixture passwords, documented hashes) have no shape an allowlist can bound.

**Keep blocking and rely on confirmation:** rejected — every verified block was a false positive, so the confirmation action became a routine click rather than a judgment.

**Classify rules in C# and pass the list to the Publisher:** rejected — rule identity would live in two places that must agree.

## Consequences

A real free-form secret that a worker copies from issue text or invents reaches the remote, and history keeps it; the pull request's generic-findings section is the only review point.
The worker testing principle that credential-shaped test values are assembled at runtime narrows to provider-format tokens, because a generic match no longer blocks.
