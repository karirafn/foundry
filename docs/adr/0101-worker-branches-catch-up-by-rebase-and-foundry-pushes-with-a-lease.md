# Worker Branches Catch Up by Rebase, and Foundry Pushes the Rewrite With a Lease

## Context

[ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md) had the agent resolve a post-run rebase conflict in a merge commit, because a rebase needs a force push and the agent must not hold one. This replaces 0087's **Conflicts** clause, and the fast-forward-only push and `<base>..HEAD` range clauses of [ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md).

On 2026-11-10 a reviewer asked a worker to "fix conflicts and rebase" (`karirafn/ruvarr` PR #450). The agent merged `origin/main` instead, citing the force-push rule. The repository allows rebase merges only (`allow_merge_commit: false`). A rebase merge replays the branch's commits without the merge commit, so the conflict came back while the provider still reported the PR `MERGEABLE`, a verdict computed for a merge. The post-run rebase step saw the branch zero commits behind and recorded `UpToDate`, so it never linearised the branch. A merge commit therefore leaves the PR unmergeable on any repository that allows only rebase merges or requires linear history.

## Decision

A worker catches its branch up with the default branch only by `git rebase origin/HEAD`, never by merging, and Foundry, never the agent, pushes the rewritten branch.

- **Agent** — every implement prompt says to catch up only by rebasing onto `origin/HEAD`, and the rebase-conflict section rebases and resolves each stop. `Bash(git merge:*)` and `Bash(git pull:*)` join the worker deny list. The entrypoint exports `GIT_EDITOR=true`, so `git rebase --continue` never waits on an editor in a container with no terminal.
- **Run commits** — the Publisher bundles, rewrites, diff-gates and empty-checks exactly `tip --not <base> <D>`, where `base` is the run-branch head and `D` the default-branch head recorded at Prepare. Commits the run pulled in from the default branch are never rewritten, so their SHAs survive.
- **Push** — a tip that descends from `base` is pushed fast-forward. A tip that descends only from `D` is a rebased tip and is pushed with `--force-with-lease=<branch>:<base>` to the run's branch only. Any other tip fails as invalid.
- **Divergence** — a rebased tip whose remote branch moved during the run is never merged, because merging restores the pre-rebase commits as duplicates plus a merge commit. The tip goes to the side branch `<branch>-run-<runId>` and reports `Diverged`, reusing ADR 0100's Needs-attention flow and its automatic exit. A provider that permanently refuses the lease push (a branch protected from force pushes) takes the same side-branch path with the reason `force-push-protected`. A retried publish would hit the same refusal, so failing would leave the issue with no exit except changing the protection.

## Considered Options

**Keep the merge commit** — works on repositories that allow merge commits. Rejected: it leaves the PR unmergeable on rebase-only and linear-history repositories, with no provider signal that says so.

**Allow the agent `git push --force-with-lease`** — rejected for the reason in ADR 0087: a prefix deny rule cannot limit the permission to the agent's own branch. Once ADR 0100 removes the agent's credential, the Publisher can scope the lease to the run's branch in code.

**Replay a human's concurrent commits onto the rebased tip** — rejected: Foundry would rewrite commits it did not produce, on a path that runs only when someone pushes during a run.

## Consequences

- This decision depends on ADR 0100. Until the agent stops holding a credential, nothing can push a rebased branch without breaking ADR 0087's guardrail.
- The post-run provider rebase in ADR 0087 still handles the clean catch-up after every run. A rebase-conflict run that rebases leaves the branch zero commits behind, which the rebase step records as `UpToDate` and which resets the conflict counter.
