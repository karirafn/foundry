# Foundry Rebases Worker Branches Through the Provider After Each Run

> **2026-11-10 — Amended:** the **Conflicts** clause in Decision (resolve in a merge commit, never rebase) is replaced by [ADR 0101](0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md). Residue missing from the start: on a repository that allows only rebase merges or requires linear history, a merge commit leaves the pull request unmergeable. The rebase step records the merged branch as `UpToDate` and never linearises it.

## Context

A worker exits with its branch based on whatever the default branch was at clone time, so a long run or a PR that waits through revisions falls behind and can conflict. The agent cannot catch the branch up by rebasing: once the branch is pushed, a rebase needs a force push, and the safety preamble (`SystemPromptBuilder.SafetyPreambleTemplate`) and the deny list (`WorkerSettingsBuilder.BaseDenyList`, `Bash(git push --force:*)`) forbid one. A prefix deny rule cannot scope a force push to "the worker's own branch only".

## Decision

Foundry, not the agent, rebases the worker branch onto the default branch after every worker run that leaves an open pull request, using the provider's server-side rebase operation through `IIssueProvider`. GitHub: the GraphQL `updatePullRequestBranch` mutation with `updateMethod: REBASE` and `expectedHeadOid`. GitLab: `PUT /projects/:id/merge_requests/:iid/rebase`. The agent's no-force-push guardrail stays intact.

- **Lifecycle state** — a worker exit with an open pull request moves the issue to `RebasePendingIssue` rather than `ChecksPendingIssue`. The issue leaves that state only once the provider shows the rebase has landed (a new head SHA on GitHub; `rebase_in_progress: false` with no `merge_error` on GitLab), so no checks verdict is ever read against the pre-rebase head. Both providers complete the rebase asynchronously.
- **Fail open** — a transient failure retries within a bound. A permanent refusal, an exhausted retry budget, or a landing wait past its timeout resolves to `ChecksPending` with a persisted `RebaseOutcome` of `Skipped(reason)`. A branch that is behind remains a valid, reviewable state, and the outcome is shown on the dashboard.
- **Conflicts** — a rebase conflict dispatches a revision run (`RevisionTrigger.RebaseConflict`). In that run the agent merges `origin/HEAD` into the branch and resolves the conflicts in a merge commit, never rebasing. A counter of consecutive conflicts (cap 2, reset by any successful sync) bounds the loop. The loop exists because a later server-side rebase drops the merge commit and re-raises the same conflicts whenever the default branch has moved again.
- **Opt-out** — the per-repository `RebaseAfterRun` toggle (default `true`) is read where the rebase runs, in the Monitoring module. When it is off, the issue resolves straight away as `Disabled`.

## Considered Options

**Instruct the agent and gate container exit in the entrypoint** — the entrypoint would check `git merge-base --is-ancestor` after `claude` exits and resume the session until the branch is in sync. This needs either an agent force push or merge-only syncing, and it ties syncing to the container's lifetime. Rejected.

**Rebase in the entrypoint shell after `claude` exits** — a deterministic `git rebase` plus `--force-with-lease` push. It works the same on every provider, but it can only run while a container is alive. Rejected in favour of a host-side operation that can run at any time.

**Allow the agent `--force-with-lease`** — this keeps history linear in conflict runs, but it weakens a guardrail for an agent that runs with `--dangerously-skip-permissions`, and the deny list cannot restrict the permission to the agent's own branch. Rejected.

## Consequences

- GitHub classifies every failure as `type: UNPROCESSABLE` and tells them apart only by message: `PullRequest::RebaseConflictError` (conflict), `There are no new commits on the base branch.` (up to date), and `head sha didn't match the current head ref.` (stale lease). These were observed by probe on 2026-11-04. The contract is string-typed, so a message the classifier does not recognise resolves as `Skipped`.
- Every successful rebase pushes a new head, so CI runs again on top of the checks the worker already watched on the pre-rebase head.
- Re-syncing open pull requests when the default branch advances later is not covered. It would force-push every open Foundry branch on each default-branch merge.
