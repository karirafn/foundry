# Workers Hold No Provider Credential; Foundry Publishes Their Work

> **2026-11-10 — Amended:** in Decision, the **Hand-off by bundle only** clause's Run bundle range and the **The token holder never runs git in agent-written state** clause's fast-forward-only push, and its divergence merge for a rebased tip, are replaced by [ADR 0101](0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md). The `<base>..HEAD` range was false from the start: once a run merges or rebases onto the default branch, the range includes the default branch's commits, and the author rewrite would give them new SHAs.

## Context

Implementation workers receive the account PAT as `GIT_PAT` for the clone and as `GH_TOKEN`/`GITLAB_TOKEN` for the whole run, and the agent pushes, opens the pull request, reads the issue, watches CI and fetches CI logs itself. The branch, force-push, comment and CI/CD-file rules exist only as prompt text (`SafetyPreamble`) and per-tool deny globs, so a prompt injection in issue or repository content holds a credential that reaches every repository the PAT can write. [ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) assigns every provider side effect to code; agent-driven publishing was the largest remaining exception. [ADR 0098](0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md) already removed the token from restricted roles but kept a clone-then-unset of `GIT_PAT` inside the worker.

## Decision

No worker container spec contains a provider credential, for any role. A model-free **Publisher** container holds the PAT for one run and performs every git transfer with the provider:

- **Hand-off by bundle only** — each run gets a per-run **Hand-off volume** holding only `in/base.bundle` and `out/run.bundle`. In **Prepare** mode the Publisher fetches the default branch (and the existing branch for revisions) into a fresh repository and writes the base bundle. The worker entrypoint clones from it into its own anonymous `/workspace` and resets `origin` to a credential-less URL. When `claude` exits, or on `SIGTERM` at a timeout, the entrypoint commits any uncommitted non-ignored changes and writes the **Run bundle** of `<base>..HEAD`.
- **The token holder never runs git in agent-written state** — hooks, `core.fsmonitor`, `url.*.insteadOf` and credential helpers in an agent-written `.git` would execute or redirect with the PAT present. In **Publish** mode the Publisher starts from a fresh repository with safe configuration, fetches the base from the remote and the Run bundle on top, rewrites author and committer to the account identity, runs the **Diff gate**, and pushes fast-forward only to the branch Foundry chose. A diverged remote branch is merged by code; a conflicting merge pushes the run tip to a side branch and moves the issue to Needs attention.
- **Diff gate** — a detected secret blocks the push (nothing reaches the remote; Needs attention). Oversized and binary files are reverted by a code-authored commit and listed in the pull request. CI/CD files are published like any other file; human review is the gate.
- **Pull request owned by code** — the implement verdict carries `pullRequest: { summary, body }`. Code builds the title and appends `Closes #N`. Later runs post one summary comment each, whose ID is recorded in a posted-comment registry that the review-feedback reader skips.
- **Run lifecycle** — publishing is a phase of the worker run (`Running → Publishing → Completed`, with `PublishFailed` retried with backoff, then Needs attention, with a TTL backstop on the Hand-off volume). `WorkerRunCompleted` carries the publish facts. Failed runs publish to the branch without opening a pull request, so a Continuation resumes from the branch.
- **Provider reads move to code** — issue body and comments are supplied at dispatch, failing-job log tails are supplied in `FailedChecks` revision prompts, and the in-run CI watch loop is removed.
- **Image** — the Publisher runs a minimal image (digest-pinned official `alpine` plus `git`) built by Foundry's startup image build, so the token-holding container ships no `claude` binary and no repository toolchain.

## Considered Options

- **Keep the token and deny `git push`/`gh` through managed settings** — rejected: a Bash indirection bypasses per-tool rules, and the token stays readable in the container.
- **Push from the Foundry host process** — rejected: it requires git on the host, temporary clone directories, and a tar upload of the clone into each worker.
- **A named workspace volume shared by Prepare, the worker and Publish** — rejected: the token holder would mount the agent-written repository, so the no-git-in-agent-state rule would rest on review rather than structure.
- **Reuse the worker image for the Publisher** — rejected: the token would sit beside `claude` and the full toolchain.
- **A pinned third-party git image** — rejected: an unassessed community image would hold the PAT.
- **The PR text as a separate Markdown file** — rejected for a schema-validated verdict field.
- **Overwrite the PR body on every run** — rejected: it discards reviewer edits.
- **Block or flag CI/CD file changes** — rejected by the operator in favour of human review; the `BlockCiCdEdits` deny globs are removed.

## Consequences

- A changed workflow file runs with repository secrets on the first push, before review, because Foundry pushes same-repository branches (GitHub runs `pull_request` workflows from the merge commit and withholds secrets only from forks). Provider-side protected variables or environments are the mitigation.
- A hard kill (OOM, `SIGKILL` after the stop grace period) produces no Run bundle; that run's unpushed work is lost.
- Each red CI check costs a container round-trip; the issue-level cap stays at two fix revisions.
- The live per-tick branch commit count is removed; the count is set from the publish facts.
- A crash between posting a summary comment and recording its ID leaves one unregistered comment, causing at most one spurious revision.
- With no provider token in the worker, provider egress can be blocked entirely (network isolation).
