# Review Criteria Can Be Covered by the Checks Gate, and the Reviewer Reads the Pull Request Description

## Context

[ADR 0113](0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md) derives `approved_for_person` whenever a criterion is `not_verifiable`, and `approved_for_person` never merges.
By 2026-11-20, 5 of the first 8 review verdicts were `approved_for_person`, each with every other criterion met and no blocking finding.
4 of those criteria claimed only that a build, test or CI run passes, which a restricted reviewer cannot run but the checks gate had already verified.
The fifth asked for content in the pull request description, which the reviewer is never shown.
This replaces the outcome-derivation clause of 0113's Decision.

## Decision

The review facts carry the checks gate's result and the live pull request description, and a criterion has a fourth status, `covered_by_checks`.

- **`covered_by_checks`** — the reviewer may answer a criterion `covered_by_checks` only when the criterion's whole claim is that a build, test or CI run passes.
  A criterion that claims anything more (a count, a named test, a behaviour) is judged from the diff as before.
  The prompt offers the status only when the checks outcome is `Passed`.
- **Code validates it against the gate** — after the run, code rewrites a `covered_by_checks` answer to `not_verifiable` when the recorded checks outcome is not `Passed`, and notes the rewrite in its evidence, before deriving the outcome.
  Facts recorded without a checks result count as not `Passed`.
- **Outcome derivation** — `covered_by_checks` counts as `met`.
  `changes_requested` and `approved_for_person` keep 0113's other conditions.
- **The pull request description is a fact** — dispatch reads the pull request body live from the provider, beside the issue content it already reads.
  The prompt shows it XML-encoded in a `<pull-request-description>` block capped at 20,000 characters, for answering criteria about the description only; its claims about the change are not evidence.
  When the read fails, the facts say so, the review proceeds, and a criterion about the description comes back `not_verifiable`.
- **Display** — the pull request comment and the dashboard show `covered by checks` as satisfied but distinct from `met`, so a person can see which criteria the reviewer delegated to the gate.

## Considered Options

- **The reviewer marks such a criterion `met` and cites the gate**: rejected because code could not tell a delegation from a reading of the code, and nothing would stop a `met` on a `NoneDetected` pull request — the merge-blocking rule would live in the prompt, which 0113 rejected.
- **Code classifies criteria by keyword before review**: rejected as brittle; "the retry passes the token" reads as a test claim.
- **Reject a `covered_by_checks` the gate does not support as `verdict_invalid`**: rejected because a rejected verdict fails open as `Skipped` and discards every finding, while the downgrade can only make the outcome less permissive and keeps a blocking finding's findings fix.
- **Allow `covered_by_checks` when the diff shows the rest of a mixed criterion**: rejected because the status would then carry a judgment code cannot check under a name that reads as code-checked.
- **Persist the composed body when Foundry opens the pull request**: rejected because it misses a person's edit and leaves pull requests opened before the change without a body.
- **Rebuild the body from the run's stored description draft**: rejected because it becomes a second producer of the pull request text.
- **Fail the dispatch when the body read fails**: rejected because it makes every review depend on a read most reviews do not need.

## Consequences

- A criterion about the description that is `unmet` cannot be fixed by a findings revision, because a revision posts a summary comment and never edits the body; it spends the findings budget and reaches `review`.
- A worker image whose schema offers `covered_by_checks` must not run against an API that does not parse it — that verdict is `verdict_invalid` and fails open as `Skipped` — so the API ships first.
