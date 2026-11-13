# DOMAIN.md

Domain concepts for Foundry — a containerized service that monitors repositories for tagged issues and dispatches sandboxed AI workers to implement them.

---

## Foundry

The service itself.
Monitors repositories across multiple providers for issues with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them.

## Worker

A Claude Code Docker container dispatched to do one job on a single issue, set by its [Worker Role](#worker-role): an implement run implements the issue, a triage run judges whether it is ready to implement (see [TriageQueuedIssue](#triagequeuedissue)).
Ephemeral — created on demand, destroyed after completion.
Has no identity independent of its WorkerRun — the container is the execution mechanism, not a separate domain concept.
Implement workers clone the repo (from the run's base bundle — see [Publisher](#publisher)), implement the issue and commit it on the run's branch; when `claude` exits (or the run is stopped) the entrypoint leaves a [Run bundle](#run-bundle), and Foundry publishes it: the Publisher pushes it and Foundry's code opens or updates the pull request. A triage worker reads the issue and returns only a verdict. No worker container spec carries a provider credential (`GIT_PAT`, `GH_TOKEN`, `GITLAB_TOKEN`) for any role ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)). Foundry discovers each run's outcome by polling the container, publishing its commits, and querying the provider.
Foundry's worker policy (the generated deny lists, `model` and the attribution suppression — `attribution` set to `{"commit": "", "pr": "", "sessionUrl": false}`, which removes Claude Code's own instruction to add AI attribution to commits and pull request descriptions; it does not stop the model writing a `Co-Authored-By` trailer or a "Generated with Claude Code" footer itself) lives in Claude Code's managed settings, `/etc/claude-code/managed-settings.json`: the container starts as root only to render that file (`root:root`, world-readable, not writable by the worker), then drops to the unprivileged `node` user before any other work, so the policy is immutable to the worker. A start that skipped the root phase, or an empty generated policy, fails bootstrap (`WorkerBootstrapFailed`, stage `managed-settings`, detail telling the operator to rebuild the worker image). The worker's user settings file `~/.claude/settings.json` is never written and is deleted at every start ([ADR 0086](docs/adr/0086-worker-policy-in-root-rendered-managed-settings.md)).

## Worker Authentication

How a worker container authenticates with the Anthropic API (Claude Code).
Two methods: API key (`ANTHROPIC_API_KEY`, pay-per-use, stored encrypted in DB) and OAuth (Max/Pro/Team/Enterprise plan, managed via a shared Docker volume).
Exactly one method is configured per Foundry instance — selected via the `AuthMode` on the single-row `ClaudeAccount` aggregate (Credentials module).

**API-key credential states.**
Only the secret is encrypted: `claude_account.auth_mode` stores plaintext JSON (the mode discriminator, plus OAuth `subscription_type`), while the API key lives in its own nullable, encrypted `claude_account.api_key` column.
The key is modelled as an `ApiKeyCredential` value object with three states — `Present` (a usable key), `NotConfigured` (never set or cleared; the seeded state of a fresh account), and `Unreadable` (ciphertext that fails to decrypt or base64-decode, the lost-key-ring case).
`Unreadable` arises only on read: a `CryptographicException` or `FormatException` while decrypting `api_key` materializes as `Unreadable` with a warning naming the column, rather than throwing and making the whole aggregate unloadable.
Because the mode discriminator is plaintext, a lost key ring never blocks loading an OAuth account or reading the mode, and the operator can always reach the repair paths (re-enter the key via `PUT /auth`, or switch to OAuth and log in).
`ClaudeAccount.CanDispatch` returns false while the key is non-`Present`, so no worker is dispatched with an empty or unusable `ANTHROPIC_API_KEY`; the state is surfaced to the operator through `ClaudeAccountSummary.ApiKeyStatus` and the settings, setup, and account-chip UI.

OAuth mode delegates the full credential lifecycle to the genuine Claude Code CLI.
Each worker mounts a Foundry-managed, shared, writable Docker volume at a dedicated credential path (`/home/node/.claude-credentials`) and points only the CLI's credential store at it (`CLAUDE_SECURESTORAGE_CONFIG_DIR`). The CLI reads, uses and auto-refreshes `.credentials.json` there, and its refresh lock is shared too, so concurrent refreshes are serialised across workers.
The OAuth credentials are the only thing workers share. The worker's Claude config dir (`CLAUDE_CONFIG_DIR` is left at its default, `/home/node/.claude`, on the container's own anonymous `/home/node` volume) is per-container. That covers `CLAUDE.md`, `skills/`, `agents/`, `commands/`, `settings.json` and `.claude.json`: anything a worker writes there is gone when its container is removed, and no concurrent or later worker loads it ([ADR 0090](docs/adr/0090-workers-share-only-the-oauth-credential-store.md)).
The login, credential-helper, Credit Probe and usage containers still mount the volume as their config dir at `/home/node/.claude`.
Foundry stores no token and injects none — the CLI is solely responsible for access-token refresh (silent, persisted to the shared volume) and for detecting refresh-token expiry.

**In-app interactive login.**
Operators seed the credential volume from within the Foundry dashboard via an interactive OAuth flow, without running any Docker command manually.
Foundry starts a dedicated login container (`foundry-claude-login` image) using the Docker API with `Tty: false`.
The container's entrypoint bootstraps a named FIFO at `/tmp/ci`, writes the FIFO's writer sleep-holder PID to `/tmp/ci.pid`, then starts `claude auth login --claudeai` — wrapped in `timeout -k 10 <session-timeout>` — with the FIFO wired to stdin so the process blocks waiting for the authorization code.
The `timeout` wrapper bounds the CLI's lifetime independently of the sleep-holder (which the code-delivery exec kills), and the container is created with `AutoRemove: true`, so it self-terminates and Docker removes it even if Foundry is not alive to tear it down — leaks cannot outlive the session timeout.
Foundry streams the container's stdout/stderr log lines and extracts the `https://…/oauth/…` authorization URL (the `visit:` line emitted by the CLI) — the URL is broadcast to the dashboard via SignalR as a `LoginSessionUpdate`.
The operator opens the URL, authorizes, and pastes the code into the dashboard.
Foundry delivers the code into the FIFO via `docker exec` (`printf '%s\n' "$C" > /tmp/ci`), then kills the sleep-holder process (read from `/tmp/ci.pid`) so the FIFO writer closes and the CLI receives EOF on stdin and proceeds to token exchange.
The service scans the log stream for the `Login successful.` signal, which is authoritative — an `Invalid code` line, or the stream closing without the success signal, is treated as failure.
It deliberately does not gate on the container exit code: the CLI may still be running when the success signal appears (observed in practice living seconds longer), so an exit-code check races the signal and misreports success as failure.
Because the login container's lifecycle at that moment is therefore unreliable (it may or may not have exited), Foundry captures the authenticated account's email, org name, and subscription type without depending on it — by running `claude auth status --json` in a fresh short-lived helper container that mounts the same credential volume read-write (`auth status` refreshes an expired access token and must be able to persist the rotated refresh token), then tears the helper down.
This volume read is also the real confirmation that login persisted a valid credential.
On an invalid code the session transitions to `Failed(InvalidCode)` and is broadcast to the dashboard for re-prompt — the operator may start a new session.

**Onboarding seed.**
Before starting `claude auth login --claudeai`, the entrypoint idempotently merges onboarding-gate flags into the volume's `.claude.json` (`hasCompletedOnboarding`, `hasTrustDialogAccepted`, `theme`), setting each key only when absent, so existing credentials and account data are never overwritten.

**Session lifecycle.**
At most one login session is active at a time — `LoginSessionService` (singleton, in-memory, Credentials module) enforces this.
Dispatch is transiently suppressed while a session is active via `ILoginSessionState.IsLoginActive` (checked by `WorkerDispatchService`); no pause record is persisted.
Session state is not persisted: a Foundry restart mid-login leaves the in-memory session gone.
In-process, `LoginSessionService.SubmitCodeAsync` tears the container down in a `finally`, so every exit path — success, invalid code, timeout, host shutdown — removes it exactly once (a second remove is a safe no-op).
For crashes where no in-process path runs, two reapers back it up: `LoginContainerReaper` (an `IHostedLifecycleService`) reaps orphaned transient containers of any state on startup — safe because no session is active yet — and `TransientContainerReaper` (a periodic background service, 60 s) sweeps only *exited* transient containers (the residue when `AutoRemove` misses), so it never removes a live login or in-flight helper while a session is active.
Both — and all transient login and credential-helper containers — key off the `foundry.transient=true` label, deliberately distinct from the `foundry.managed`/`foundry.worker-run-id` labels on long-lived worker containers, so reaping never touches a running worker.
A session times out if no URL is captured within the URL timeout, if no code is submitted within the session timeout, or if login confirmation is not seen within the sign-in timeout after the code is delivered; these surface as typed `LoginFailureReason` variants (`UrlTimeout`, `CodeTimeout`).

**Auth-invalid pause.**
When a worker exits with an auth failure, Foundry classifies the run as `AuthInvalid` and raises an **auth-invalid pause**: dispatch pauses, the affected issue is retried automatically on resume, and resume is triggered automatically by a successful in-app login: `LoginSuccessCommitter` commits the identity to `ClaudeAccount` and publishes `CredentialsValidated`, on which the Issues module re-queues every issue that failed with the auth-invalid reason.
The auth-invalid state lives on `ClaudeAccount.Validity` (`CredentialValidity.Invalid`), not on Global Settings, and `ClaudeAccount.CanDispatch` refuses dispatch while it holds.
There is deliberately no auto-resume timer for auth-invalid — Foundry cannot detect a successful re-login without an explicit in-app login session completing.
Settings and the setup wizard derive OAuth status from persisted state — a committed account identity (set by a successful in-app login) reads as signed-in, an auth-invalid pause reads as re-login-needed — rather than from a live credential-volume read; token expiry is deliberately not surfaced (the CLI auto-refreshes it in place, so a captured value would be stale and misleading), and they never assert an unverified "valid" status.
The OAuth credential sits in plaintext in the volume, consistent with how the genuine CLI stores it, and bounded by the Docker-socket trust boundary Foundry already operates within.
Distinct from provider authentication (Account / PAT), which authenticates git operations against GitHub or GitLab.

## Worker Image Build

The staged Docker build (base → worker → login → [Publisher](#publisher) images) that produces the images workers run on, executed by `WorkerImageRebuildService` (a Workers-module background service).
A failure at any stage — including the login and Publisher images — fails the whole rebuild.
Rebuilds are requested when the Worker Image Configuration changes (`WorkerImageSettingsChanged` → immediate rebuild request) and by operator retry; an immediate rebuild request supersedes a pending backoff wait.
When building is disabled, any image-settings change instead triggers a Docker image **inspect** on the configured tag: if the image exists on the Docker host the build state moves to `Idle`; if it is absent the state moves to `Failed` with the message `image '<tag>' not found on the Docker host`.
Startup and operator retry also re-run the inspect when building is disabled.
While no usable image exists and at least one account is configured, the dashboard blocks with a full-screen forge overlay (**cold build**) until the first build succeeds.

## Image Build State

A closed-hierarchy value object on Global Settings tracking the worker image build lifecycle: `Idle`, `Building`, `Failed(ErrorTail, NextRetryAt, Attempt)`.
Transitions are methods on the aggregate (`BeginImageBuild`, `CompleteImageBuild`, `FailImageBuild`); persisted as a JSON blob.
Failed builds auto-retry with exponential backoff (`initial * 2^(attempt-1)`, capped at a configured maximum); operator retry is only permitted from `Failed`.
A non-`Idle` state drives the dashboard image-build banner and disables the worker-image settings form, and dispatch gates on the status — no new workers are dispatched while `Building` or `Failed` (including the case where a pre-built image is absent when building is disabled).

## Worker Image Configuration

The worker image settings stored on Global Settings: the **image tag** (default `foundry-worker:local`; empty or `:latest` rejected) and the **build toggle** (default enabled), alongside the set of preinstall flags for optional toolchains (e.g. `INSTALL_DOTNET`, `INSTALL_ANGULAR`, `INSTALL_CHROMIUM`, `INSTALL_DOCKER`) that drive Docker build args.
Any change publishes `WorkerImageSettingsChanged`; with build enabled this requests an immediate rebuild, with build disabled it triggers an inspect (see Worker Image Build).

### Docker-Enabled Worker Run Spec

When `InstallDocker` is true, the worker runs its own nested rootless `dockerd` so Testcontainers-based integration tests work inside the container.
This requires the relaxed allowance set from [ADR 0079](docs/adr/0079-relaxed-allowances-for-nested-rootless-docker.md): `CAP_SYS_ADMIN`, `seccomp=unconfined`, `apparmor=unconfined`, unmasked `/proc` system paths (empty `MaskedPaths`/`ReadonlyPaths` — the Engine API form of the CLI's `systempaths=unconfined`), and the `/dev/net/tun` device.
A named per-run data-root volume (`foundry-worker-docker-<workerRunId>`, labelled `foundry.managed=true` + `foundry.worker-run-id=<id>`) is mounted at `/home/node/.local/share/docker`; the nested daemon uses native overlayfs on this volume, so `/dev/fuse` is not required.
The isolation boundary weakens: the worker runs as uid 1000 with no route to container root under normal operation, but `CAP_SYS_ADMIN` plus unconfined seccomp/AppArmor mean that code inside the worker that escalates to root can reach the Docker host (on Docker Desktop, the WSL VM).
On hosts where the daemon cannot start, the entrypoint logs `FOUNDRY_DOCKER_UNAVAILABLE`, leaves `DOCKER_HOST` unset, and the run continues in degraded mode — authoring integration tests that execute where a daemon is available (CI, native Linux).
Workers built without `InstallDocker` receive an unchanged run spec and entrypoint.

## Publisher

A short-lived, model-free container that holds the provider token for one worker run ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)). **Prepare** mode builds the run's base bundle before the worker starts; **Publish** mode turns the worker's run bundle into a pushed branch and, on success, an opened or updated pull request. **Probe** mode tells whether a paused provider host can be reached again (see [Host Pause](#host-pause)).
Its image (`foundry-publisher:local`, `workers/publisher/`) is the official `alpine` image pinned by digest plus `git` — no `claude` CLI and no repository toolchain — built by the [Worker Image Build](#worker-image-build).
It runs under the worker hardening ([ADR 0088](docs/adr/0088-worker-containers-run-hardened-by-default.md)) with no capability added back, as an unprivileged user, with a read-only root filesystem and anonymous volumes at `/work` (its scratch repository), `/tmp` and `/var/tmp`. Git runs in a fresh repository with `core.hooksPath=/dev/null` and no system or global configuration; the token reaches git only through `GIT_ASKPASS`.
**Prepare** fetches the default branch (and, for a Continuation or Revision run, the run's existing branch) and writes `in/base.bundle` to the [Hand-off volume](#hand-off-volume), then reports the run's **base** on one stdout line, `FOUNDRY_BASE default_branch=<name> default_sha=<sha> [branch_sha=<sha>]`, which Foundry persists on the WorkerRun (`base_default_branch_head`, `base_run_branch_head`). Its `ls-remote` retries a provider it cannot reach — `classify_error` reads git's stderr as `unreachable` when it carries git's libcurl transport prefix `unable to access` with no HTTP status or with 429 or a 5xx (DNS failure, refused or timed-out connection, the provider shedding load) — up to 3 more times, 5 s, 15 s and 40 s apart (`UNREACHABLE_RETRY_DELAYS`), printing one stderr line per retry and no sentinel; a retry that succeeds continues as if the first attempt had. When every attempt is unreachable it prints the sentinel with stage `unreachable` (`detail=could not reach the repository: <git's last error>`), and the run fails `ProviderUnreachable` (category `provider_unreachable`), which its issue carries into its failed state. That failure also trips a [Host Pause](#host-pause) for the repository's host, saved with the run's failure. Any other failure (token rejected, run branch missing, empty repository, or an `ls-remote` failure that is not `unreachable`, including one met on a retry) is not retried, prints the bootstrap sentinel with stage `clone`, and the run fails `WorkerBootstrapFailed`. Either way no worker container starts.
**Probe** runs one `ls-remote HEAD` against `CLONE_URL`, with the token offered through `GIT_ASKPASS` in a fresh scratch repository exactly as Prepare offers it, under the same hardening, and no Hand-off volume. It prints one stdout line, `FOUNDRY_PROBE result=reachable|unreachable detail=<one line>`: `unreachable` is exactly `classify_error`'s `unreachable`, and every other outcome, success or failure (a rejected token, a missing repository), is `reachable`, because the provider answered. It has no in-script retry: the host pause's probe schedule is the retry.
**Publish** runs once the run's container has ended, with the PAT resolved by repository at publish time and the Hand-off volume, of which it reads only `out/run.bundle`. It never runs git in agent-written state: it fetches the run branch and the default branch into a fresh repository, verifies the bundle and fetches its tip into a Foundry-owned ref. The tip must descend from the run-branch head Foundry persisted at Prepare (`<base>`, a forward tip) or, rebased, from the default-branch head persisted there (`<D>`, a rebased tip) — both passed in, never read from the volume; a tip that descends from neither fails as `Failed(invalid, permanent)` and pushes nothing ([ADR 0101](docs/adr/0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md)). A tip that descends from both (a merge from before ADR 0101, or a human merge) is forward. Every [Run commit](#run-commits) is rewritten to the account identity (author and committer, merge commits preserved) and stripped of every `Co-Authored-By` trailer and the Claude Code "Generated with" footer; default-branch commits the run merged in keep their SHAs. A tip whose remote branch still equals the base is pushed fast-forward only (a rebased tip with `--force-with-lease` on the base, [ADR 0101](docs/adr/0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md)), to the run's branch and nowhere else. When the remote branch moved during the run, a forward tip's clean merge is pushed fast-forward and a conflicting one pushes the run's tip to `<branch>-run-<runId>` instead; a rebased tip is never merged (that would restore the pre-rebase commits as duplicates) and goes to the side branch. When the provider permanently refuses the lease push because the branch is protected from force pushes, the rebased tip goes to the side branch too, reported with `reason=force-push-protected`: a retried publish would hit the same refusal, so this path never ends in `PublishFailed`. A lease the remote moved under is transient, so the retry re-fetches and diverges. It reports one stdout line, `FOUNDRY_PUBLISH result=published|nothing_to_publish|diverged|failed …` (`published` with the head and the branch's commit count, `diverged` with both branches and, for a refused force push, `reason=force-push-protected`, `failed` with `kind=permanent|transient|unreachable` and a reason; publish has no in-script retry, and a provider `classify_error` finds `unreachable` is reported `kind=unreachable`, which Foundry retries like `transient` and which also trips a [Host Pause](#host-pause) for the repository's host), preceded by one `FOUNDRY_RUN_COMMIT <sha> <subject>` line per pushed Run commit.

## Branch Divergence Reason

Why the [Publisher](#publisher) pushed a run's work to its side branch `<branch>-run-<runId>` instead of the run's branch (`BranchDivergenceReason`, stored as its token in `worker_runs.divergence_reason` and `issues.divergence_reason`; rows recorded before it was kept are `branch-moved`):
- **`BranchMoved`** (`branch-moved`) — someone pushed to the branch during the run, and the run's work could not join it: merging a forward tip conflicted, or the tip was rebased.
- **`ForcePushProtected`** (`force-push-protected`) — the provider refused the lease push of a rebased tip because the branch is protected from force pushes ([ADR 0101](docs/adr/0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md)). The issue detail's diverged callout says so and guides replacing the issue branch with the side branch rather than merging it.

It travels on `PublishResult.Diverged`, on the run (`WorkerRun.Divergence`), on `RunPublishFacts` and on `BranchDivergedIssue`, and the issue detail exposes it as `stateDetails.divergenceReason`.

## Hand-off volume

The per-run named volume (`foundry-handoff-<workerRunId>`, labelled `foundry.managed=true` + `foundry.worker-run-id=<id>`) shared by the [Publisher](#publisher) and the worker. It holds only `in/base.bundle` and `out/run.bundle`, never a working repository; the worker mounts it at `/handoff`, clones the base bundle into its own `/workspace`, points `origin` at the credential-less clone URL and `origin/HEAD` at the default branch, and writes its [Run bundle](#run-bundle) to `out/` (Prepare makes `out/` writable for the unprivileged worker).
It is created at dispatch and outlives the worker's container: it is deleted once the run is published or discarded. An unpublished run's volume is kept for at most its time to live (`PublishRetryPolicy.HandOffTimeToLive`, 7 days); then it is deleted and the run discarded (`FailureReason.PublishExpired`, category `publish_expired`). An orphan — a volume whose run neither holds a dispatch slot nor unpublished work — is reaped on the same cadence and rules as a data-root volume.

## Run bundle

The `git bundle` of a run's [Run commits](#run-commits), written by the worker entrypoint to `/handoff/out/run.bundle` when `claude` exits (any exit code) or when the run is stopped (a `SIGTERM` trap stops `claude` within the worker stop grace period, `DockerWorkerOrchestrator.WorkerStopGracePeriodSeconds`). Before bundling, a dirty tree is committed once as `wip: uncommitted changes at run end` (`git add -A`, so gitignored output stays out). No bundle is written when the range is empty. It is the only output of a worker run that reaches the provider; a hard kill (OOM, `SIGKILL`) produces none.

## Run commits

The commits a worker run produced: those reachable from the run tip but from neither SHA Foundry recorded at Prepare, the run-branch head (`base`) and the default-branch head (`D`) — `tip --not <base> <D>`. The entrypoint bundles `HEAD --not origin/<branch> origin/HEAD` (just `--not origin/HEAD` for a Fresh run). The Run bundle carries exactly these, the Publisher rewrites only these, and an empty set is `NothingToPublish`.

## Diff gate

The checks the [Publisher](#publisher) runs on a [Run bundle](#run-bundle) before pushing (#788): a **secret block** plus **size and binary reverts**. After the identity rewrite it scans the content added by the [Run commits](#run-commits) with gitleaks (pinned by version and checksum in the Publisher image) using Foundry's own configuration (`workers/publisher/gitleaks.toml`); a repository's `.gitleaks.toml`, `.gitleaksignore` and `gitleaks:allow` comments are never honoured. A finding pushes nothing: the Publisher reports `FOUNDRY_SECRET_FINDING commit=<sha> line=<n> rule=<id> path=<path>` lines (never the matched value) and `FOUNDRY_PUBLISH result=secret_detected findings=<n>`, the run becomes `PublishBlockedRun` keeping its Hand-off volume, and its issue waits in Needs you as `publish_blocked` with only **Discard run** (there is no publish-anyway). Otherwise every file the Run commits added or changed that is binary by git's detection or larger than 5 MiB is restored, in one code-authored commit `chore: revert files blocked by the Foundry diff gate`, to its content at `<D>` for a rebased tip and at `<base>` otherwise (deleted when it did not exist there), reported as `FOUNDRY_REVERTED_FILE reason=binary|oversized size=<bytes> path=<path>`, kept on the run (`RevertedFiles`) and listed under "Files reverted by Foundry" in the pull request body or the run's summary comment. Deletions and CI/CD files are not gated.

## Provider

A git hosting platform (GitHub, GitLab).
Each provider has its own API client, label format (flat on GitHub, scoped on GitLab), and CLI tooling (`gh`, `glab`).
The provider is carried on `ClaimedIssueDispatch` so the worker dispatch knows which CLI tooling and credential env var apply.
Inside the worker, the provider CLI is registered as git's credential helper (e.g. `gh auth setup-git`), authenticated by the Account PAT injected as the provider-specific token env var (`GH_TOKEN` for GitHub, `GITLAB_TOKEN` for GitLab) — this authenticates both `git push` and PR/MR creation without embedding the PAT in the repo's git config.
The provider CLIs (`gh`, `glab`) are unconditionally baked into the worker base image (`Dockerfile.base`); when `GH_TOKEN`/`GITLAB_TOKEN` is set but the corresponding CLI is absent, the entrypoint fails bootstrap (`stage=auth`) rather than warning and continuing.

## Provider Rate Budget

The finite pool of API requests a provider grants per window.
A per-provider, per-budget concept tracked independently for each budget key.

| Budget | Limit |
|---|---|
| GitHub REST, authenticated user | 5,000 req/hour |
| GitHub GraphQL, authenticated user | 5,000 points/hour (separate budget; cost = requests/100, min 1) |
| GitHub content creation (secondary) | 80/min and 500/hour |
| GitHub per-endpoint points (secondary) | 900/min REST, 2,000/min GraphQL; GET = 1, POST/PATCH/PUT/DELETE = 5 |
| GitHub concurrency (secondary) | 100 in flight |
| gitlab.com authenticated API | 2,000 req/min |
| Conditional GET returning 304 | does not count against the GitHub primary limit |

### Rate Budget Reading

The last observed `remaining` for one budget, together with its `limit`, `resetAt`, and `ObservedAt` (the wall-clock time Foundry recorded the reading).
Readings are in-memory only — they are not persisted across restarts.
A reading whose `ObservedAt` age exceeds the staleness window is treated as stale.

### Provider Budget Health

A three-valued verdict produced by the Provider Budget Policy from a reading and a floor:

- **`Healthy`** — a fresh reading with `remaining >= floor`.
- **`Low`** — a fresh reading with `remaining < floor`. Logged once on transition; surfaced on the dashboard. Polling continues — the governor does not block.
- **`Unknown`** — the reading is absent or stale. Never treated as exhaustion (fail open).

GitHub REST and GitHub GraphQL produce independent verdicts; a `Low` on one does not gate the other.
GitLab headroom is recorded for visibility but produces no verdict (not evaluated against a floor).

### Fixed Per-Cycle Cost

The count of provider API calls a poll cycle issues that does **not** scale with issue count.
Bounded by `RepositoryPoller.MaxFixedPollCallsPerCycle`.
ADR 0066 and this glossary reference this constant by name rather than restating its value — there is one place to change it and the change is reviewed at the constant.
A compile-time invariance test asserts that driving `RepositoryPoller.PollAsync` with 5 issues and with 200 issues produces identical total provider-call counts, and that the fixed-cost scenario stays within `MaxFixedPollCallsPerCycle`.

The governor's enforcement is observability and early warning; the compile-time invariance test is the actual enforcement layer.
See [ADR 0066](docs/adr/0066-provider-api-request-budget-governor.md).

## Branch Protection

A provider-agnostic set of preconditions Foundry requires on a repository's default branch before processing issues.
Three invariants: rejects direct pushes (all changes via merge/pull request), rejects force pushes (history cannot be rewritten), rejects deletion.
Each provider adapter maps these invariants to provider-specific API checks (GitHub branch protection rules, GitLab protected branches).
Validated per repository on each poll cycle and at repository creation, with the result stored on the Monitored Repository as its Repository Eligibility. Dispatch is gated on the repository's eligibility; individual issues are not re-validated.

## Account

A credential for accessing a specific provider's API — one Account per PAT ("Account" in the UI, `Credential` in the domain).
Modeled as polymorphic variants (`GitHubCredential`, `GitLabCredential`) — each provider may carry provider-specific configuration (e.g., API base URL for self-hosted instances).
The PAT is stored encrypted in the database using Data Protection API + EF Core Value Converters.
Multiple accounts can exist per provider and per host — including multiple PATs authenticating as the same provider user. Accounts do not reference repositories; repositories resolve to an account through Namespace Claims.

### Branch-Rules Token

An optional, server-only credential on an Account (`Credential.BranchRulesToken`, the account's *server-side token*), used solely for server-side privileged provider writes — default-branch protection and [Webhook Registration](#webhook-registration) — and never given to workers.
It needs GitHub fine-grained **Administration: write** (branch rules) and **Webhooks: read and write** (webhooks), or the GitLab **Maintainer** role (both) — privileges the worker-facing account token must not carry, because that token is injected into every worker container.
Stored encrypted with the same mechanism as the PAT (`ProviderToken`), never returned by the API (only `CredentialSummary.BranchRulesTokenStatus`: `present`, `absent` or `unreadable`), and set, replaced (repairing an unreadable one) or cleared through the account update path. Rotating the account PAT preserves it.
An unreadable branch-rules token never affects the account's eligibility. Its permissions are not validated on save — they are established by attempting the write.

### PAT Owner

The provider username the token authenticates as — resolved from the provider's `/user` endpoint at token validation and stored as the account's name (`Credential.Name`).
Not user-chosen: the account's display name *is* the PAT owner. Because multiple accounts may share one PAT owner, the name alone does not identify an account; UI surfaces disambiguate with the account's Namespace Claims and host.

### Account Identity

An account is identified by `(PAT Owner, derived owner-namespace set, host)`.
The login alone is not enough, because one login legitimately holds several tokens — GitHub fine-grained PATs are each bound to a single resource owner, so reaching two owners requires two tokens and therefore two accounts.
A request is a **duplicate account** when another credential on the same host carries the same `Credential.Name` *and* its Namespace Claims cover the *entire* incoming token's derived owner namespace set — leaving no unclaimed namespace the new account could exclusively hold.
Overlap is tested on exact namespace values.
Both the create and update paths apply the same never-steal semantics: same-login sibling namespaces are subtracted from the conflict set and never offered for transfer.
On create, a partial overlap with a same-login sibling is not a duplicate — the account is created claiming only the unclaimed namespaces; the sibling's namespaces are silently skipped.
On a token-bearing update the same qualifier applies: the rotation is rejected only when the same-login sibling covers the entire derived set, leaving the rotated account with nothing of its own.
A partial overlap on update reduces to the account's own namespaces via the never-steal subtraction, so a token spanning both owners still rotates cleanly.
Same login with disjoint owners is permitted — that is the normal shape, not an exception.
A *different* login reaching an already-claimed owner is not a duplicate; it is a Namespace Claim conflict, resolved through takeover.
Detection is server-side only, in the create and update handlers: the derived owner set requires a provider repository listing, so no client can compute it.
On a token-bearing update, derivation runs and both guards evaluate *before* the credential is mutated, so a rejected rotation persists nothing; a derivation that returns `Unavailable` rejects the update rather than proceeding unguarded (see [ADR 0064](docs/adr/0064-account-identity-keyed-on-login-owner-set-and-host.md)).

### Token Validation Outcome

A closed variant set (`TokenValidationOutcome`) returned by both provider adapters after validating a PAT.
Five variants:

- `Authenticated(AccountName, MissingScopes)` — the token authenticated and the PAT owner resolved; `MissingScopes` lists any required scopes absent from the token.
- `AuthenticationFailed` — the token was rejected (401/unauthorized).
- `ScopesUnverifiable(AccountName)` — the token authenticated and the owner resolved, but the token's scopes could not be read. Triggered by: GitLab group/project access tokens where `personal_access_tokens/self` returns non-2xx; and GitHub tokens served without an `X-OAuth-Scopes` response header that are **not** fine-grained PATs (e.g. GHES classic tokens, server configurations that suppress the header). GitHub fine-grained PATs (`github_pat_` prefix) are classified by prefix at validation time and, when served without `X-OAuth-Scopes`, return `Authenticated` with empty `MissingScopes` instead — no caution. Fine-grained permission enforcement is delegated to the provider and the create-time write probe (`IGitHubWriteProber`); see [ADR 0053](docs/adr/0053-github-token-type-by-prefix.md).
- `IdentityUnresolved` — the response could not be parsed to an identity (neither provider's identity field present).
- `ProviderMismatch(DetectedProvider)` — the response carried the *other* provider's identity shape (e.g. GitLab answered while GitHub was selected); names the detected provider.

Two behavioral rules apply:

1. A successful authentication requires a PAT owner — a "valid but nameless" state cannot be represented; if the owner cannot be resolved the outcome is `IdentityUnresolved` or `ProviderMismatch`, not `Authenticated`.
2. `ScopesUnverifiable` warns but permits saving — the provider owns authorization and claim-time scope enforcement is the real gate, so an unreadable scope list is advisory, not blocking.

The endpoint (`POST /api/accounts/validate-token`) maps these variants to the response `Kind` field: `"authenticated"`, `"authenticationFailed"`, `"scopesUnverifiable"`, `"identityUnresolved"`, `"providerMismatch"`.

### Unreadable Token

A stored `accounts.token` value that cannot be decrypted — the Data Protection key that encrypted it was lost or rotated (`CryptographicException`), or the stored ciphertext is not valid base64 (`FormatException`).
Modeled as the `Unreadable` variant of the `ProviderToken` value object (`Present(Value)` / `Unreadable`), materialized by `ProviderTokenConverter` when decryption fails — never an empty-string sentinel.
An unreadable token is distinct from an **absent** token: the token column is null when no token is configured, and `Unreadable` when a token exists but cannot be read. Both report `HasToken = true` only for `Unreadable` (a value is present); the finer state is carried by the `TokenStatus` field (`"present"` / `"absent"` / `"unreadable"`).
An account whose token is `Unreadable` sends no provider request: eligibility evaluation and every polling path short-circuit before calling the provider, so the account cannot silently degrade to anonymous access. Affected repositories report the credential-specific reason `credential-unreadable` rather than masquerading as inaccessible or unpushable.
Materialization logs a warning naming `accounts.token` and the credential id. The state is repaired by re-entering the token through the account update path — the degraded credential loads normally and the re-entry never reads the old token, so no manual database edit is required.

## Namespace Claim

The exclusive association between an Account and an owner namespace on a host — stored in `credential_namespaces` with a unique `(host, namespace)` constraint, so each namespace is served by exactly one account.
Claims are derived from the token's writable-repository listing (every distinct owner of a repo the token can push to) at account creation, token rotation, and repository recheck.
A Monitored Repository carries no account reference; on each eligibility evaluation the covering account is resolved by matching the repository's owner against claims (`ICredentialResolver`). A repository whose owner no account claims is Ineligible (`no-credential:<namespace>`).

## Global Settings

A strongly-typed single-row entity storing all UI-configurable settings.
Includes worker settings (max concurrent, timeout), the Worker Image Configuration (image tag, build toggle, and toolchain flags) and Image Build State, the Worker Resource Limits (memory MB, CPU count, PID ceiling), the Claude Code Settings (additional deny rules; the model is a per-role setting, see Worker Role), the credit-probe interval (`ProbeIntervalMinutes`, default 60, validated to a minimum of 5), and dispatch pause controls: usage-limit controls (`AutoResumeOnUsageReset`, `UsageLimitResetsAt`), the separate manual operator pause (`IsDispatchPaused`), and the [Host Pauses](#host-pause) (`HostPauses`, one `HostPause(Host, PausedAt, ProbeAttempts, NextProbeAt)` per unreachable provider host, a JSON column `host_pauses`). Keeping every pause reason on this one row means `ResumeAllDispatch` clears them all in one method and the dispatcher reads them in one `DispatchPauseState` (`PausedHosts` beside the global flags); `GET /api/settings` returns them as `pausedHosts` (`host`, `pausedAt`, `nextProbeAt`, `probeAttempts`).
Worker prompts are not settings: they ship in the worker image with each Worker Role (see Worker Role).
Additional deny rules are appended after the always-enforced base deny list.
The auth-invalid pause is not stored here — it is `ClaudeAccount.Validity` in the Credentials module (see Worker Authentication).
`ProbeIntervalMinutes` governs how long the credit block waits between probes (see Credit Probe); the operator sets it in the dispatch-settings form, `GlobalSettings.UpdateProbeInterval` rejects a below-minimum value, and the credit-probe scheduler reads it via `IGlobalSettingsQueries.GetProbeIntervalMinutesAsync` at each arm.
DB is the single source of truth — it is the only source for worker settings; `IConfiguration` is not consulted for any setting the UI manages, and the `Workers` configuration section no longer exists.
Webhook ingress settings ([ADR 0085](docs/adr/0085-webhooks-nudge-the-poll-loop.md)): the optional **Webhook Public Base URL** (`WebhookPublicBaseUrl`, absolute `https` only, no query or fragment — rejected otherwise with `Settings.InvalidWebhookPublicBaseUrl`) and the [Backstop Interval](#backstop-interval) (`BackstopIntervalMinutes`, default 15, 5..1440, `Settings.InvalidBackstopInterval`), both edited in the dispatch settings page through `PUT /api/settings/webhooks`. The webhook port itself is host configuration (`Webhooks:Port`), not a Global Setting.

## Monitor

The background process that polls configured repositories for issues labeled `foundry`.
Runs on a fixed tick interval (30s default) and checks whether each repo is due for polling based on its scheduled interval — the [Backstop Interval](#backstop-interval) while its [Webhook Status](#webhook-status) is `Active` and webhook ingress is configured, otherwise its effective poll interval (falling back to the global default when unset) — and its `LastPolledAt` timestamp.
It is also the only consumer of the [Nudge](#nudge) channel: a nudge wakes it immediately, and nudged passes run in the same loop as scheduled polls, so a repository is never polled concurrently. A scheduled poll composes the same per-pass `RepositoryPoller` entry points a nudge runs (listing, dependency, review, checks, branch-rules step), so behaviour is identical whether a pass is nudged or scheduled.
On the first scheduled tick after process start (clean restart or crash) it runs a one-time **startup catch-up**: every active repository is polled once as a scheduled poll (listing, dependency, review, checks and branch-rules passes, plus the rebase step backstop) regardless of `LastPolledAt`, because webhook deliveries that arrived while Foundry was down are rejected and never redelivered. The catch-up flag is in-memory and consumed by that tick, so a repository whose catch-up poll fails returns to its normal schedule, and a change the catch-up finds is never a [Missed Change](#missed-change). Later ticks poll only repositories that are due.
The per-cycle provider call cost is bounded by `RepositoryPoller.MaxFixedPollCallsPerCycle` (see [Provider Rate Budget](#provider-rate-budget)) and enforced by the poll-call invariance test; the nudge rate cap on event-triggered runs is enforced by the sibling nudge-call-cap test; deadline nudges run at their deadline outside that cap, and each deadline producer bounds its own re-arming.

### Webhook Delivery

One verified inbound HTTP request from a provider (see [ADR 0085](docs/adr/0085-webhooks-nudge-the-poll-loop.md)). Deliveries arrive only on the dedicated webhook port (`Webhooks:Port`), at `/webhooks/github/{hookId}` or `/webhooks/gitlab/{hookId}`; a two-way port guard returns 404 for any other path on that port and for `/webhooks/*` on the main port. Verification fails fast before the body is parsed: an unknown hook id is 404; a missing or mismatched secret is 401 — GitHub's `X-Hub-Signature-256` (HMAC-SHA256 of the raw body under the repository's secret) or GitLab's `X-Gitlab-Token` (the plain secret token), both compared in constant time; and a body naming another repository (GitHub `repository.full_name`, GitLab `project.path_with_namespace` including nested groups, case-insensitive) is 400. A verified delivery returns 202 without calling the provider, after enqueueing at most one Nudge. Deliveries touching nothing Foundry tracks are dropped (the relevance filter).

### Webhook Status

Per-repository webhook health: `NotConfigured` (no webhook port or no public base URL — derived, never stored) / `NotRegistered` / `Pending` / `Active` / `Silent`. Only `Active` uses the Backstop Interval. The operator's "I've added the webhook" action and a successful [Webhook Registration](#webhook-registration) set `Pending` (an `Active` hook stays `Active`); any verified delivery (including GitHub's `ping`; GitLab has none, so a GitLab hook stays `Pending` until its first delivery — typically the hook form's "Test" action) moves `Pending` or `Silent` to `Active`; a [Missed Change](#missed-change) moves `Active` to `Silent`.

### Webhook Registration

Foundry creating or updating a repository's provider webhook itself (`WebhookRegistrar`), with the Account's [Branch-Rules Token](#branch-rules-token) — never the worker token. A hook whose payload URL matches (current, or the previous one after a public base URL change) is updated in place — URL, secret, JSON content type, exactly the mapped event list (GitHub events; GitLab trigger flags, with every other trigger including push events explicitly off) and the active flag — and adopted; otherwise one is created (GitHub `POST/PATCH /repos/{o}/{r}/hooks`, GitLab `POST/PUT /projects/:id/hooks`).
Runs when a repository is added; on the repository page's "Register webhook" action (`POST /api/accounts/{accountId}/repositories/{id}/webhook/register`); when the Account's Branch-Rules Token is set or replaced (its covered `NotRegistered` repositories); when the Webhook Public Base URL changes to a new value (`WebhookPublicBaseUrlChanged`, every repository whose status is not `NotRegistered`); and once on each transition into `Silent` — a hook still `Silent` afterwards is not retried until its next transition into `Silent`. Multi-repository triggers run sequentially and one failure never stops the rest.
Outcome (returned, never persisted): `created` / `updated` (status becomes `Pending`), `skipped` (`not-configured`, `no-server-token`, `server-token-unreadable` — no provider call) or `failed` (`insufficient-permission` for GitHub 403/404 on `/hooks` or GitLab 403; `provider-error` otherwise). Skipped or failed leaves the status unchanged, so a `NotRegistered` repository keeps showing the manual instructions; polling is never blocked. Hooks are never deleted (removing a repository leaves its hook).

### Nudge

A request to run one poll pass for `(repository, pass, issue?)` now — or not before a deadline. It is caused by a relevant delivery or a deadline, coalesced (a ~2 s window; pending nudges for one key run once) and rate-capped (each key runs event-triggered nudges at most once per the repository's normal effective poll interval: a leading run, then one coalesced trailing run at the end of that interval), and never changes domain state itself. A not-before (deadline) nudge runs at its deadline, outside the rate cap: it neither waits out the cap window nor stamps it, and a capped event-triggered nudge pending for the same key runs with it. Every deadline producer must therefore be self-bounded (a finite number of deadlines per episode or batch); the [Provider Rate Budget](#provider-rate-budget) still caps total calls underneath. Nudges live in a bounded in-memory channel; a full channel drops the new nudge and a restart loses pending ones — the backstop poll recovers both. Event mapping: `issues` → listing, except that an `issues` close of a blocker of a tracked issue nudges the listing plus dependency pass even when the blocker is neither tracked nor labeled (GitHub sends no `issue_dependencies` delivery when a blocker closes); `issue_dependencies` → listing + dependency pass; `pull_request`, `pull_request_review`, `pull_request_review_comment`, `issue_comment` (on a PR) → review pass for the owning issue (checks pass when it is checks-pending); `check_suite` (completed), `status` → checks pass; `branch_protection_rule`, `repository_ruleset` → branch-rules step; `push` → no nudge (a push to a running worker's branch is published as the ephemeral `ProviderBranchPushed`); `ping` → none. GitLab events are read from the body's `object_kind` and nudge the same passes: `issue` / `work_item` → listing (plus the dependency pass when the closing issue blocks a tracked issue); `merge_request`, `note` (on a merge request) → review pass for the owning issue (checks pass when it is checks-pending); `pipeline` (finished status) → checks pass; `push` → no nudge, as for GitHub. Confidential issues and comments map like their public counterparts. GitLab has no event for issue links or protected branches, so it never nudges the dependency pass otherwise or the branch-rules step. An issue entering checks-pending raises `IssueEnteredChecksPending`, which schedules a checks nudge immediately plus not-before nudges at the grace-window and upper-bound deadlines. An issue entering rebase-pending raises `IssueEnteredRebasePending`, which nudges the rebase pass immediately; the rebase step schedules its own not-before nudges while it waits for a rebase to land or backs off a transient failure.

### Backstop Interval

The poll interval for repositories whose webhook is `Active` (Global Settings, default 15 minutes) — never shorter than the repository's own effective poll interval.

### Missed Change

A change found by a **scheduled** poll (other than the startup catch-up, see [Monitor](#monitor)), in a category the provider has a webhook event for, that no delivery announced. The categories are issue detection, detail change or untrack; a closed issue or pull request or new review feedback; a provider-driven checks verdict; and — on GitHub only — a branch-rule change (the `Eligible`/`Ineligible` outcome of the default branch's rules; transitions through `Unreachable`, a denied write probe or a missing credential do not count). It moves `Active` to `Silent`, so the next poll uses the normal interval. Changes without a mapped event on that provider — time-based checks exits, dependency refreshes, blockers in another repository, and GitLab protected-branch changes — never count; while `Active` they are picked up within the Backstop Interval.

## Issue

A provider-side issue tagged for Foundry processing.
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `TriageQueuedIssue`, `TriageBlockedIssue`, `TriageInProgressIssue`, `TriageFailedIssue`, `BlockedIssue`, `FreshQueuedIssue`, `ContinuationQueuedIssue`, `RevisionQueuedIssue`, `InProgressIssue`, `RevisionInProgressIssue`, `RebasePendingIssue`, `ChecksPendingIssue`, `ReviewIssue`, `AwaitingInputIssue`, `UnchangedIssue`, `CompletedIssue`, `FailedIssue`, `ContinuableFailedIssue`, `RevisionFailedIssue`).
Every variant carries the shared properties, including the append-only [clarification log](#clarification) and the latest [Triage Verdict](#triage-verdict), through every transition.
Every new issue is triaged once before its first implement run, and only once its blockers close: `Detected → (TriageBlocked →) TriageQueued → TriageInProgress →` the dependency check (`FreshQueued` or `Blocked`) or `AwaitingInput` ([ADR 0097](docs/adr/0097-every-new-issue-is-triaged-before-dispatch.md), ordering amended by [ADR 0102](docs/adr/0102-triage-runs-once-blockers-clear.md)).
State transitions are methods on each variant that return the next variant type, enforcing valid transitions at compile time.

### Issue Lifecycle Partition

The lifecycle splits into two partitions defined by `IssueStateRegistry` (server) and mirrored in `ACTIVE_STATES` / `RESOLVED_STATES` (frontend):

- **Active** — every state except `completed`.
- **Resolved** — `completed` only.

The dashboard shows each issue in one of four display groups. They come from the placement the server declares ([Issue Stage](#issue-stage), [Needs action](#needs-action), [Awaits dispatch](#awaits-dispatch)), not from a frontend list of states:

- **Needs you** — Active with [Needs action](#needs-action): it progresses only after a person acts (`review`, `awaiting_input`, `unchanged`, `failed`, `continuable_failed`, `revision_failed`, `triage_failed`, `publish_failed`, `publish_blocked`, `branch_diverged`). On the board (`/`) it sits in the Needs-you row of its stage's column.
- **Queued** — Active, [Awaits dispatch](#awaits-dispatch) and no Needs action: waiting for a worker run to be dispatched (`detected`, `triage_queued`, `triage_blocked`, `queued`, `blocked`, `revision_queued`, `continuation_queued`). It sits in its stage's queue section.
- **In flow** — every other Active issue: a worker is running (`in_progress`, `revision_in_progress`, `triage_in_progress`, exactly `LIVE_STATES`) or Foundry's own code is on a [Code step](#code-step) (`rebase_pending`, `checks_pending`). It sits in its stage's in-flow cell.
- **Resolved** — stage Done (`completed`). It leaves the board and is listed on Resolved history (`/issues`), newest first, next to the run statistics.

A few states deserve explicit note:

- `unchanged` needs action because the worker produced no changes and the user must decide whether to retry — it cannot resolve itself.
- `blocked` is Queued because a `BlockedIssue` auto-transitions to `FreshQueuedIssue` when its blockers close on the provider; no user action is required.
- `triage_blocked` is Queued for the same reason: a `TriageBlockedIssue` auto-transitions to `TriageQueuedIssue` when its blockers close.
- `awaiting_input` needs action (label "Awaiting input") because it advances only when a person answers the posted questions. Its column follows its resume target ([Issue Stage](#issue-stage)).
- `triage_failed` needs action (label "Triage failed"): a transient failure retries itself, anything else waits for Retry triage or Skip triage ([ADR 0014](docs/adr/0014-remove-immediate-requeue-always-pause.md)).

### Issue Stage

The part of the lifecycle that currently owns an issue — **Triage**, **Implement**, **Awaiting review** (an open pull request waiting for a person), or **Done**. Declared by each state type (`Issue.Stage`), so a new state cannot compile without one. `awaiting_input` follows its resume target: a re-triage is in Triage, every other target in Implement. The board (`/`) shows one column per stage except Done; a completed issue leaves the board. Served on `IssueSummary.Stage` as `triage | implement | awaiting_review | done`.

### Needs action

True when the issue progresses only after a person acts (`Issue.NeedsAction`). A Needs-action issue is in the Needs you display group: it sits in its stage's Needs-you row on the board.

### Awaits dispatch

True when the issue is waiting for a worker run to be dispatched — queued, or detected or blocked before it can be queued (`detected`, `triage_queued`, `triage_blocked`, `queued`, `blocked`, `revision_queued`, `continuation_queued`). An awaiting-dispatch issue sits in its stage's queue section on the board.

### Waits on blockers

True when the issue waits for its open blockers to close before it can move on: `blocked` (triaged or skipped triage, then found blocked) and `triage_blocked` (waiting untriaged). Declared by each state type (`Issue.WaitsOnBlockers`), so a new state cannot compile without one; every other state is false. Both blocked states also [Await dispatch](#awaits-dispatch); a client placing an issue gives Waits on blockers precedence. Served on `IssueSummary.WaitsOnBlockers`.

### Entered state at

When the issue entered its current state (`Issue.EnteredStateAt`): the detection time for a `detected` issue, otherwise the time of the transition that created the current state instance. Every transition stamps it anew, a re-queue into the same state type included, and it is never carried over from the state being left. Each state's factory takes the transition time as a required parameter, so no transition can leave it unstamped. For a working state (`in_progress`, `revision_in_progress`, `triage_in_progress`) it answers "running since", to within container start-up. Issues that existed before it was recorded start from their detection time, an upper bound until their next transition. Served on `IssueSummary.EnteredStateAt`.

### Code step

The step Foundry's own code is running for the issue, with no worker involved — **publishing** (or **retrying a publish**), **rebasing** (`rebase_pending`), or waiting for CI **checks** (`checks_pending`). Rebasing and checks are declared by the state type (`Issue.CodeStep`); publishing is a phase of the issue's run, so `IssueSummary.CodeStep` reads it from the run's publish phase (`publishing | publish_retrying | rebasing | checks`, or null).

## TriageQueuedIssue

`triage_queued`: an issue waiting for a worker to triage it ([ADR 0097](docs/adr/0097-every-new-issue-is-triaged-before-dispatch.md)). A [Queued Issue](#queued-issue) with `TierRank = 2`, between continuation and fresh. Display group: Queued, label "Triage queued".

- **Ways in** — every new issue (`DetectedIssue.QueueTriage()`, on creation; an issue still `detected` from before triage existed is sent there by the dependency pass when it has no open blockers), a [TriageBlockedIssue](#triageblockedissue) whose blockers closed, input arriving on an `AwaitingInputIssue` with a `Retriage` [resume target](#awaitinginputissue), Retry triage from `TriageFailedIssue`, and [Re-triage](#re-triage) from `FreshQueued` or `Blocked`.
- Triage waits for open blockers (ADR 0102): when the dependency pass or the claim-time re-check finds open blockers, `Block()` → `TriageBlockedIssue`. Re-triage or an answered question while blockers are open therefore lands here first and then moves to `TriageBlocked`. The dependency pass keeps blockers current in `TriageInProgress`, `TriageFailed` and `AwaitingInput` too, where they are only recorded. Revision, continuation and rebase-conflict runs never go through triage.
- `Context` is `DispatchContext.Triage`, whose role maps to `WorkerRole.Triage`; `DispatchBranchName` is `null`, so no branch name is generated and the run gets none.
- **Ways out** — `Claim()` → `TriageInProgressIssue`; `Block()` → `TriageBlockedIssue`. Hard-deleted on untrack like every queued state.

## TriageBlockedIssue

`triage_blocked`: an issue that hasn't been triaged and has open blockers, or one a triage run found blocked ([ADR 0102](docs/adr/0102-triage-runs-once-blockers-clear.md), [ADR 0103](docs/adr/0103-triage-defers-on-open-dependencies-by-linking-blockers.md)). When its blockers close, it moves to `TriageQueued`. A [Blocked Issue](#blocked-issue) has already been triaged or skipped triage, and moves to `FreshQueued`. Display group: Queued, label "Triage blocked".

- Carries `WorkerRunId`: the triage run whose verdict named the blockers; `null` when the issue waits on blockers found before triage.
- Not a [Queued Issue](#queued-issue): it is never a dispatch candidate and holds no worker slot. Triage waits so the run judges the issue against the code its blockers wrote, not code that doesn't exist yet.
- **Ways in** — the dependency pass applying open blockers to a `DetectedIssue` (`BlockTriage()`) or a `TriageQueuedIssue` (`Block()`), or the claim-time re-check finding them on a `TriageQueuedIssue`. Raises `IssueTriageBlocked`.
  - A `blocked` triage verdict: `TriageInProgressIssue.MarkBlocked(verdict)` (`FromTriageInProgress`) records the [Triage Verdict](#triage-verdict) and sets the blockers to the ones the verdict names, then those already recorded. Raises `IssueTriageBlocked` and `TriageBlockersNamed`, which the Issues bridge turns into the `TriageBlockersFound` integration event; Monitoring links each named blocker on the provider and posts the [Blocker Reason](#blocker-reason) comment.
- **Ways out** — `Unblock()` → `TriageQueuedIssue`, raising `IssueTriageQueued`, when the dependency pass finds its blockers closed or unlinked (as for `BlockedIssue`); `FailBlockerLink(workerRunId, blockerIssueNumber, rejection, failedAt)` → `TriageFailedIssue` with `FailureCategory.BlockerLinkFailed` and `IssueTriageFailed`, when the provider permanently refuses to link a blocker the verdict named (`BlockerLinkFailed`, handled by `BlockerLinkFailedHandler`; ignored, with a log line naming which part did not match, unless the issue is still `TriageBlocked`, the event's run is the state's `WorkerRunId` and the verdict names the blocker). `FailBlockerLink` flattens the provider's text to one line and caps the stored reason at `TriageBlockedIssue.FailureReasonMaxLength`. The verdict and blockers stay on the failed issue. No operator actions. Hard-deleted on untrack.

## TriageInProgressIssue

`triage_in_progress`: an issue a triage worker is reading. Carries `WorkerRunId`. Display group: In flow, label "Triaging".
Counts against the global slots and the [Repository Worker Limit](#repository-worker-limit) like every running worker.

- **Ways out** — the run's outcome step dispatches by role: a triage run that exits 0 with a schema-valid verdict publishes `TriageRunCompleted(IssueId, WorkerRunId, TriageResult, DecidedAt)` with `TriageResult = Ready(kind) | NeedsInput(kind, questions) | Blocked(kind, blockedBy)` instead of `WorkerRunCompleted`, and the merge-request lookup never runs.
  - `Ready` → `MarkReady(verdict)` records the [Triage Verdict](#triage-verdict) and goes through the dependency check: `FreshQueued` with no open blockers, `Blocked` otherwise.
  - `NeedsInput` → `MarkNeedsInput(verdict)` records the verdict and enters `AwaitingInputIssue` (`FromTriageInProgress`) with the `Retriage` resume target; its questions are posted on the issue thread.
  - `Blocked` → before any cycle check, `TriageVerdict.Blocked.RejectionOf` rejects a result with no blockers, more than the blocker cap, a blocker named twice, a blank reason or a reason over the reason cap; the handler routes it to `TriageFailedIssue` with `FailureCategory.VerdictInvalid` directly, not via `WorkerRunFailed`. Otherwise the handler asks `DependencyCycleDetector.FindFirstCycleClosedBy` over the stored edges of the repository's other issues whether any named blocker closes a cycle. The detector searches the stored edges for a path from each named blocker back to the issue; a blocker that is the issue itself is a cycle `#n → #n`. None does → `MarkBlocked(verdict)` records the verdict and enters `TriageBlockedIssue` (`FromTriageInProgress`), and the named blockers are linked on the provider (see [TriageBlockedIssue](#triageblockedissue)). One does → the `blocker_cycle` route: `FailOnBlockerCycle(cycle, failedAt)` → `TriageFailedIssue` with `FailureCategory.BlockerCycle`, no link written, and a reason that names the cycle and tells the person to remove one of its links before retrying triage.
  - Any other end — a missing or invalid verdict (`FailureReason.VerdictInvalid`, category `verdict_invalid`: a step failure, never a fallback to facts, [ADR 0094](docs/adr/0094-code-decides-claude-judges-only-what-code-cannot.md)), a non-zero exit, a timeout or a cancellation — goes through `WorkerRunFailed` to `TriageFailedIssue`.
- A `TriageRunCompleted` for an issue no longer in triage with that run (a redelivery, or the issue moved on) is ignored; with the inbox dedup this makes it replay-safe.

## TriageFailedIssue

`triage_failed`: an issue whose triage run failed. Carries `WorkerRunId`, `FailureReason`, `FailureCategory` and `FailedAt`. Display group: Needs you.
Not a `FailedIssue`, whose `Retry()` returns `FreshQueuedIssue` and would skip triage.

- **Causes** — beyond a failed run (non-zero exit, timeout, cancellation, `verdict_invalid`): `blocker_cycle` (`FailureCategory.BlockerCycle`) when a blocker a `blocked` verdict names would close a dependency cycle (from `TriageInProgressIssue.FailOnBlockerCycle`), and `blocker_link_failed` (`FailureCategory.BlockerLinkFailed`) when the provider permanently refused to link a named blocker (from `TriageBlockedIssue.FailBlockerLink`, via `FromTriageBlocked`, which takes the run id from the `BlockerLinkFailed` event). Neither is transient, so both park until Retry triage or Skip triage.
- **Ways out** — `RetryTriage()` → `TriageQueuedIssue` (`POST /api/issues/{id}/retry-triage`); [Skip triage](#skip-triage) (`POST /api/issues/{id}/skip-triage`); a failure whose category is auto-retryable (`transient_api_error`, `provider_unreachable`) retries automatically back to `TriageQueuedIssue` through [`TransientRetryService`](#transient-retry), on that category's own schedule. Any other reason parks it. Hard-deleted on untrack, like the other failed states.

## Triage Verdict

The latest verdict a triage run returned for an issue (`Issue.TriageVerdict`, column `triage_verdict`, `null` until first triaged): `Ready(kind, decidedAt)`, `NeedsInput(kind, questions, decidedAt)` or `Blocked(kind, blockedBy, decidedAt)`, where `blockedBy` is a non-empty list of at most `TriageVerdict.Blocked.MaxBlockers` `TriageBlocker(issueNumber, reason)` naming each open issue once, each with a [Blocker Reason](#blocker-reason) of at most `TriageVerdict.Blocked.ReasonMaxLength` characters. Carried through every transition; a later triage replaces it.
`null` means not yet triaged or a stored verdict that could not be read: an unreadable row loads as no verdict so one bad row cannot make every issue in the repository unreadable. The next state change copies the null into a new row and so drops the unreadable text. A SQL filter on `TriageVerdict == null` does not match such a row before then, because the column itself is not NULL. A verdict is a cached judgement and every consumer tolerates null.
Its kind decides the issue's [Kind Classification](#kind-classification) only while no label does. Exposed on the issue detail as `triage: {outcome, kind, questions, decidedAt}`, where `outcome` is `ready`, `needs_input` or `blocked` (a `blocked` verdict carries no questions; its blockers and reasons appear as `blockedBy: [{issueNumber, reason}]` on the state details, `reason` being `null` for a blocker a person linked or when the latest verdict names none).

## Re-triage

The operator action that sends a `FreshQueuedIssue` or `BlockedIssue` back to `TriageQueuedIssue` (`POST /api/issues/{id}/retriage`; any other state is 409), keeping its recorded blockers. While those blockers are open it then waits as a [TriageBlockedIssue](#triageblockedissue), and is triaged once they close. There is no automatic re-triage once an issue is ready: an edit to a queued or blocked issue does not trigger triage again.

## Skip triage

The operator action that sends a `TriageFailedIssue` through the dependency check without a readiness verdict (`FreshQueued` with no open blockers, `Blocked` otherwise), its kind left as it is: an unclassified issue stays `Unclassified` and gets `feat/`. Any other state is 409. It is fail-open on purpose, for a broken triage role.

## AwaitingInputIssue

`awaiting_input`: an issue whose run stopped to ask a person one or more questions ([ADR 0096](docs/adr/0096-awaiting-input-replies-detected-by-recorded-ids-and-branch-head.md)).
It waits for input on the thread where the question was posted. Display group: Needs you, label "Awaiting input".

- **Way in** — an implement run that exits 0 with a schema-valid `needs_input` verdict resolves to `WorkerOutcome.NeedsInput` (a merged pull request still completes the issue and the verdict is ignored; a non-zero exit still goes through failure classification), and `WorkerRunCompleted` carries the questions. `InProgressIssue.MarkAwaitingInput` and `RevisionInProgressIssue.MarkAwaitingInput` open a [clarification](#clarification) round.
- **Resume target** — recorded at entry from what the asking run left: `Revision(branch, pr)` for an open pull request (always for a revision run), `Continuation(branch)` for commits without a pull request, `Fresh` when nothing was pushed, or `Retriage` when a triage run asked (see [TriageInProgressIssue](#triageinprogressissue)) (column `resume_target`).
- **Question post** — the closed union `Pending | Posted(commentId, postedAt, commentUrl) | Failed(reason, nextAttemptAt, attempts)` (column `question_post`). The state is committed first; the awaiting-input step of each poll (and its `AwaitingInput` [nudge](#nudge)) then posts the questions on the pull request when there is one, otherwise on the issue, and records the comment in the [posted-comment registry](#posted-comment-registry) in the same save that marks it `Posted`. A transient failure retries with exponential backoff (1 minute doubling, capped at 1 hour); a permanent one is not retried automatically.
- **Ways out** — [input arrived](#input-arrived) resumes along the resume target: `RevisionQueued` with `RevisionTrigger.InputProvided`, `ContinuationQueued` on the branch with an empty failure reason, or `FreshQueued`, or `TriageQueued` for `Retriage` (the triage prompt renders the clarification log). As in the review pass, a closed provider issue completes it and a pull request closed without merging fails it continuably (`PrClosed`).

## Clarification

One round on an issue's append-only clarification log (`Issue.Clarifications`, column `clarifications`): the questions, the input that arrived, and when each happened (`askedAt`, `answeredAt`).
A round is open until input arrives; a round left when the issue moved on without input (closed, or its pull request closed) stays unanswered.
Every implement prompt (fresh, continuation, revision) and every triage prompt renders the log in its own section, so an answer lasts as long as the issue does; with an empty log the prompt is unchanged.

## Recommendation

The single option a run proposes for one of its questions, with its reason (`InputQuestion.Recommendation`, the shared verdict definition's required, non-empty `recommendation`). A reply that leaves the question unaddressed accepts it, so a bare "ok" accepts every recommendation and a reply answering by number accepts the rest.
Code never applies it: the posted question comment shows it as **Recommended:**, the issue detail shows it after the question's context, and the clarification log renders it as `<recommendation>` with the rule stated, so the next run reads the reply and the recommendation together. Null only for questions stored before runs returned recommendations; those render without it, and their comment keeps the plain reply instruction.

## Input Arrived

After the question's post time, a new eligible comment on the question's thread whose ID Foundry did not post: not a bot, not a system note, not in a resolved thread, not in the [posted-comment registry](#posted-comment-registry), whoever the author is (the PAT owner's own reply counts).
Settled after the 2-minute quiet period of `ActionableFeedbackPolicy`; comments still inside it schedule a follow-up `AwaitingInput` nudge. The issue thread is read with the [issue-thread reader](#issue-thread-reader), a pull request thread with `GetReviewFeedbackAsync`, both with the post time as the cutoff. Several comments that settle together are joined, oldest first, into the round's input (`AwaitingInputArrived`).

## Issue Kind

A value object classifying the nature of the work — `Feature`, `Bug`, `Refactor`, `Documentation`.
Extracted during issue detection by a provider-agnostic classifier (`LabelClassifier`): each label is normalized by stripping any `scope::` prefix, then the suffix is matched against the kind names in priority order (bug, feature, refactor, documentation) — so flat (`feature`) and scoped (`type::feature`) labels both classify on either provider.
When no recognized label is present the classifier returns no kind (`IssueDetected.IssueKindLabel` and `IssueDetailsChanged.IssueKindLabel` are `null`); `IssueKind.FromLabel` returns `null` for an unrecognized label rather than defaulting.

### Kind Classification

The closed value object on the base `Issue` recording how the issue's kind was decided: `Labelled(IssueKind)` when a kind label matched, `Triaged(IssueKind)` when no label did and a [Triage Verdict](#triage-verdict) judged it, otherwise `Unclassified`.
Precedence: a label beats triage. It is derived from two halves carried through every state transition: the label half, set at detection and re-derived from the labels whenever they change (`UpdateDetails`), and the latest triage verdict's kind. A label change re-derives only the label half, so a label added later beats `Triaged`, and removing the last kind label from a triaged issue leaves it `Triaged` — an already-generated branch name is unaffected. When a label decided and triage judged a different kind, the triage kind is exposed as `conflictingTriageKind` on the issue detail's `kind: {source, kind, conflictingTriageKind}`; nothing acts on it.
The label half is persisted in `issues.issue_kind`: the labelled kind, or `NULL` for no label. Rows that existed before the classification was introduced are read as `Labelled` with their stored kind.
The `Feature` fallback is applied only by `BranchName.Generate(KindClassification, …)`, which derives the branch prefix (`feat/`, `fix/`, `refactor/`, `docs/`) and resolves `Unclassified` to `IssueKind.Feature` (`feat/`); `Triaged` uses the triaged kind.

## Monitored Repository

A repository configured for Foundry to poll.
Resolves its serving Account through the Namespace Claim on its owner (no stored account reference) and specifies an optional per-repo poll interval; when unset, the repo polls at the global `PollIntervalSeconds` from Global Settings.
Its effective poll interval — the per-repo value when set, otherwise the inherited global default — is computed on the aggregate (`EffectivePollInterval`) and surfaced on the repository summary (`EffectivePollIntervalSeconds`, plus a `PollIntervalIsDefault` marker) so the dashboard can show the applied interval and flag it as the inherited default rather than a blank.
Uniquely identified by the pair (Host, Repository Slug) — the same repo on the same host cannot be monitored twice (prevents duplicate issue detection), while the same path on different hosts (e.g. github.com vs gitlab.com, or self-hosted instances) refers to distinct repositories.
Tracks `LastPolledAt` for per-repo poll timing; nudged passes do not stamp it.
Its scheduled interval also depends on its [Webhook Status](#webhook-status): while `Active` (and ingress is configured) it polls at the [Backstop Interval](#backstop-interval), otherwise at its effective poll interval.
Carries a random opaque webhook hook id (addressing its payload URL) and a Foundry-generated webhook secret, encrypted at rest with the same DataProtection mechanism as provider tokens. The secret is never returned in list responses — only by the explicit reveal endpoint (`POST /api/accounts/{accountId}/repositories/{id}/webhook/secret`) used for manual registration.
Carries a Repository Eligibility status, re-evaluated on each poll cycle.
Carries `MaxConcurrentWorkers` (non-nullable `int`, range 1..20, default 1) — see [Repository Worker Limit](#repository-worker-limit).
Carries `RebaseAfterRun` (non-nullable `bool`, default `true`) — see [Rebase After Run](#rebase-after-run).

### Apply Branch Rules

The persisted operator intent on a Monitored Repository (`ApplyBranchRules`, non-nullable, default `true` on the add request; existing rows migrated to `false`) that Foundry may write its default-branch rules.
Acted on only on explicit operator action (`BranchRuleEnforcer`): when the repository is added, when the Account's Branch-Rules Token is set or replaced (every opted-in repository the Account covers, processed sequentially; one failure never stops the rest or fails the account update; clearing the token, or an update that leaves it untouched, writes nothing), or through the repository card's "Apply branch rules" action (`POST /api/accounts/{accountId}/repositories/{id}/apply-branch-rules`), which also flips the persisted intent to `true` — an explicit opt-in, even for a repo opted out at add time. That action is offered only when the repository is `Ineligible` solely through `branch-protection:*` violations, and only for a repository the account serves (same host, covered by its Namespace Claims). Polling stays read-only toward the provider and never writes or repairs rules.

### Rebase After Run

The per-repository operator toggle (`RebaseAfterRun`, non-nullable, default `true` for new repositories; existing rows migrated to `true`) that turns the post-run rebase on or off (see [Rebase Pending Issue](#rebase-pending-issue), [ADR 0087](docs/adr/0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md)).
Set on the add request (`rebaseAfterRun`, default `true`) and toggled from the repository form through the update request (`rebaseAfterRun`; omitted leaves it unchanged); surfaced on the repository summary.
Read by the Monitoring module's rebase step when it runs — never snapshotted, so a change mid-run wins. When off, a rebase-pending issue resolves straight away with `RebaseOutcome.Disabled`.
Unlike [Apply Branch Rules](#apply-branch-rules), existing rows default to `true`: the rebase writes only Foundry-owned branches, never repository-wide protection.

### Branch Rules Outcome

The per-request result of an apply attempt, returned in the add response (`CreateRepositoryResponse.branchRules`) and the apply-branch-rules response (`ApplyBranchRulesResponse.branchRules`, where `not-requested` cannot occur) and never persisted: `applied`, `already-present`, `not-requested`, `skipped` (`access-failure`, `unverifiable`, `no-rules-token`) or `failed` (`insufficient-permission`, `provider-error`).
The write is attempted only when the full evaluation is `Ineligible` solely through `branch-protection:*` violations and the account has a readable Branch-Rules Token. The rules are then re-read (no second write probe) and the stored eligibility reflects that re-read, never the write's status code — a write that succeeds but does not take effect is `failed` (`provider-error`).
A failed or skipped apply never fails the add. On the token-save path the per-repository outcomes are not returned; the account update's affected repositories report each repository's eligibility after enforcement.
GitHub: Foundry upserts one repository ruleset named `foundry-eligibility` targeting `~DEFAULT_BRANCH` (`pull_request` with zero required approvals, `non_fast_forward`, `deletion`, no bypass actors). GitLab: Foundry protects the default branch (push: No one, merge: Maintainers, no force push), or patches an existing protection in place — it never unprotects the branch.

## Available Repository

A candidate shown in the add-repository picker — the listing that answers "which repos can *this* account monitor". Three orthogonal facts about a repo are distinguished and must not be conflated:

- **Visible** — the account's token can see the repo (it appears in the provider's repository listing at all).
- **Writable** — the token can push to it (`CanPush`). A visible-but-not-writable repo is shown disabled with a "no write access" reason, so the operator can rotate the PAT to unlock it rather than have it silently hidden.
- **Claimed** — the repo's owner namespace is covered by one of *this* account's Namespace Claims, tested with `Namespace.IsPrefixOf` (a claim on a parent namespace covers child paths, so a GitLab group claim covers its nested subgroups and projects).

The picker rule: the listing (`GET /api/accounts/{accountId}/repositories/available-repositories`) returns only repos that are both visible and claimed by the selected account. This makes the picker truthful — a repo it offers is one the account will actually resolve and serve at monitor time. Writable-but-unclaimed repos are excluded; claimed-but-read-only repos are shown disabled.

Each entry carries an `IsMonitored` flag, set when a Monitored Repository already exists for the same (Host, Repository Slug) pair. Monitored entries render a check mark, are non-selectable, and expose a screen-reader "already monitored" label — matched on Host + Slug, so the same slug monitored on a different host is not flagged. The monitored state wins over the read-only state: a repo that is both already monitored and non-writable shows the monitored check, not the "no write access" affordance.

An empty listing is disambiguated for the operator: an account with **no** Namespace Claims yields an explanatory "no claimed namespaces" state, distinct from a claimed-but-empty result and from a token/load failure — the response carries a `HasClaims` flag so the picker can tell these apart rather than showing a bare empty list.

Accepted asymmetry: a repo visible to account A but namespace-claimed by account B (whose own token cannot see it) is unaddable under A — A's listing excludes it because A does not claim its namespace, and B's listing excludes it because B's token cannot see it. This is intended: B is the account that would serve the repo at monitor time, and if B's token cannot reach it, monitoring would fail there anyway.

## Repository Priority

The relative ordering of monitored repositories, expressed as a 0-based, contiguous, unique `Position` integer on each Monitored Repository — lower value means higher priority.
`Position` is one component of Dispatch Order and therefore affects both dispatch and the order the dashboard's queued-issue list is served in.
New repositories append at the end (highest existing Position + 1); deleting a repository renumbers survivors contiguously.
Polling is independent of position.

## Repository Worker Limit

The maximum number of workers that may run concurrently for a single monitored repository.
Modeled as `MaxConcurrentWorkers` — a non-nullable `int` on `MonitoredRepository`, constrained to the range 1..20, defaulting to 1 (see [ADR 0074](docs/adr/0074-per-repository-capacity-in-candidate-selection.md)).
Enforced in `DispatchCandidateSelector` (Issues module) — not at the Workers-side dispatch gate — because the gate receives an anonymous capacity token and cannot see which repository will spend it; see ADR 0074 for the architectural rationale.
The in-flight count against this limit is `count(InProgressIssue) + count(RevisionInProgressIssue) + count(TriageInProgressIssue)` grouped by repository, computed Issues-side as its own query (`InFlightWorkerCountQuery`).
Issues in review (`ReviewIssue`) do not count; fresh, revision and triage workers all count, so the limit means "workers", not "implement runs".
This count is independent of the global `MaxConcurrent` gate and of the Workers-side [Worker Slot Occupancy](#worker-slot-occupancy) (which counts reservations, starting runs, and active runs but carries no repository identity).
Lowering the limit below the current in-flight count does not preempt running workers — the repository drains to the new limit as its workers finish.

## Dispatch Order

The total order that governs which queued issue is claimed next and the order the dashboard list query serves queued issues in.
The comparison primitive is the four-tuple `DispatchOrderKey` — `(TierRank, Position, IssueNumber, Id)`, evaluated in that precedence:

- **TierRank** — a property of the queued state variant that encodes dispatch-priority class: revision (0) < continuation (1) < triage (2) < fresh (3). Lower rank is claimed first; this is the primary ordering criterion.
- **Position** — the `EligibleRepository.Position` of the repository that owns the issue, supplied externally into the key. Lower position is preferred within a tier.
- **IssueNumber** — the provider's issue number (the per-project `iid` on GitLab), lowest first within the same repository and tier, reflecting author intent rather than when Foundry noticed the issue (see [ADR 0092](docs/adr/0092-order-same-repository-queued-issues-by-issue-number.md)). `DetectedAt` is not a key component.
- **Id** — guarantees a deterministic total order when all other fields are equal.

The single shared artifact consumed by both the dispatcher and the dashboard is a **capacity-aware ordering** over this key (`CapacityAwareDispatchOrder`): a multi-pass walk over the `DispatchOrderKey`-sorted sequence that carries each repository's remaining headroom.
An issue whose repository has remaining headroom takes the next place and decrements the headroom; an issue whose repository is at its [Repository Worker Limit](#repository-worker-limit) defers to a subsequent pass.
The selector (`DispatchCandidateSelector`) takes the claimable head of this sequence; the dashboard query renders the sequence and derives Queue Position by index.
The two consumers feed the walk different headroom inputs via the `DispatchHeadroom` value object:

- The selector uses **claim headroom** (`DispatchHeadroom.ForClaim`): `max(0, MaxConcurrentWorkers − inFlight)` — headroom as it actually is right now.
- The dashboard query uses **forecast headroom** (`DispatchHeadroom.ForForecast`): under **global saturation** (total in-flight across all repositories ≥ the global `MaxConcurrent`), every repository with an in-flight run gets `max(0, MaxConcurrentWorkers − (inFlight − 1))`, modelling the one running worker whose completion opens the next global slot; repositories with no in-flight run keep their full `MaxConcurrentWorkers`. Below global saturation forecast equals claim, so ordering is unchanged.

The two cannot disagree about order at the moment the list is served — the same walk function produces both results.

The dashboard treats the server array order as the only source of queue order and reconciles it by refetching `GET /api/issues`, rather than deriving order locally or receiving a pushed order (see [ADR 0067](docs/adr/0067-dashboard-reconciles-queue-order-rather-than-deriving-it.md)).
Every `IssueUpdated` event schedules a debounced reconcile (300 ms) alongside the counts refetch that already runs on that path, collapsing a burst of events into one request.
A low-frequency safety-net timer (30 s) schedules the same reconcile, covering order inputs that raise no issue event — including a repository `Position` change in Settings.
The reconcile fetch carries a latest-wins request token, so a late response cannot install a stale order.
A failed reconcile sets a `queueOrderStale` signal to disclose bounded staleness without showing the blocking load-error banner; the next successful reconcile clears it.
The automatic exit from the possibly-stale state is the next event-driven or timer-driven reconcile, plus the full reload on SignalR reconnect.

The dashboard partitions queued issues into two groups before applying the key: eligible-repository issues (real `Position`) rank above ineligible-repository issues (sentinel `int.MaxValue` position), each retaining its `RepositoryEligibilityStatus` for display.

## Queue Position

A queued issue's 1-based index in the capacity-aware ordering (see [Dispatch Order](#dispatch-order)), counted over **dispatchable queued issues only** — queued-tier issues whose repository is eligible (not `ineligible` or `unreachable`).

Queue Position is derived from the rendered dispatchable-queued array on the dashboard (see [ADR 0067](docs/adr/0067-dashboard-reconciles-queue-order-rather-than-deriving-it.md)).
It is never persisted and never transported from the server; the ordinal indexes the very array the cards are rendered from, so it cannot disagree with the on-screen sequence.
Because the capacity-aware ordering is the shared artifact, Queue Position reflects what the dispatcher will actually claim next under saturation — a queued issue that belongs to a saturated repository defers in the ordering (and therefore in position) until headroom opens, even though its repository is eligible.
Under **global saturation**, Queue Position 1 forecasts the repository whose in-flight worker's completion opens the next global slot, keeping **Next up** truthful against the dispatcher's actual next claim.
Without this forecast, an idle repository's issue would appear at Position 1 while the dispatcher actually claims from the repository whose worker just finished.

Not-dispatchable queued issues — those whose repository is ineligible or unreachable — have no Queue Position and their card gutter shows `—`.
A queued issue belonging to a repository at its [Repository Worker Limit](#repository-worker-limit) still has a Queue Position — its repository is eligible; only the position reflects the deferral.

Queue Position 1 identifies the **Next up** issue: the one the dispatcher claims next when a worker slot becomes available.

## Dispatch Context

The sealed union describing the nature of the work being dispatched to a worker — assembled on the aggregate, not in the handler.
Four variants:

- **`Fresh`** — a new implementation attempt; carries `BranchName`.
- **`Revision`** — a revision cycle; carries `BranchName`, `PullRequestUrl`, and a polymorphic [Revision Trigger](#revision-trigger) describing why the revision was queued.
- **`Continuation`** — resuming an existing branch after a failed run; carries `BranchName` and optional `FailureReason`.
- **`Triage`** — a triage run ([ADR 0097](docs/adr/0097-every-new-issue-is-triaged-before-dispatch.md)); carries nothing, and its `Role` is `DispatchRole.Triage`. The run gets no branch.

`DispatchContext` is assembled by the concrete `QueuedIssue` subtype overriding the abstract `Context` property — `FreshQueuedIssue` returns `Fresh`, `RevisionQueuedIssue` returns `Revision`, `ContinuationQueuedIssue` returns `Continuation`, `TriageQueuedIssue` returns `Triage`.
The handler and claimer treat `Context` as an opaque value — they forward it into `ClaimedIssueDispatch` without switching on it.
Survives the outbox round-trip via `[JsonPolymorphic]` / `[JsonDerivedType]` annotations (see [ADR 0051](docs/adr/0051-dispatch-context-union-and-typed-dispatch-seam.md)).

## Tracker Context

The issues Foundry reads from the provider at dispatch, alongside the issue's own content.
It holds the [Referenced Issues](#referenced-issue) and the [Open-Issue Index](#open-issue-index), rendered into every role's prompt as untrusted data ([ADR 0104](docs/adr/0104-runs-receive-the-tracker-context-from-code.md)).

## Referenced Issue

A same-project issue that the issue links to, or mentions as `#N` in its body or comments.
Each one carries its relation (`blocked_by`, `blocks`, `relates_to` or `mention`), title, state, labels, closing merge request state and body, with the body capped at 16 KiB.
`blocked_by` is an [Issue Dependency](#issue-dependency).
"Referenced" follows GitHub's and GitLab's own "referenced" / cross-reference wording.

## Open-Issue Index

The number and title of each open issue in the repository, excluding the issue itself.

## Revision Trigger

Why a revision run was queued — a polymorphic value carried by `RevisionQueuedIssue`, `RevisionInProgressIssue`, and `RevisionFailedIssue` (and surfaced on `DispatchContext.Revision`) in place of comment-specific fields.
Four variants:

- **`ReviewFeedback`** — a human reviewer requested changes; carries `Comments`, `OmittedCommentCount`, and `NewestConsumedCommentAt`.
- **`FailedChecks`** — the PR's provider checks went red within the fix budget; carries the `FailingChecks`.
- **`RebaseConflict`** (`$type` `rebase_conflict`) — the post-run rebase conflicted with the default branch within the conflict budget (see [Rebase Conflict Attempts](#rebase-conflict-attempts)); payload-free. The worker rebases the branch onto the default branch with `git rebase origin/HEAD`, resolves every conflict at each stop (preserving both the branch's intent and the default branch's changes), continues with `git rebase --continue` and runs the tests; it discovers the conflicting paths from `git rebase` itself and never merges ([ADR 0101](docs/adr/0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md)). The [Publisher](#publisher) pushes the rebased tip with a lease. A rebase that resolves every conflict by taking the default branch drops every run commit, so the publish is `NothingToPublish`; a run that rebases cleanly leaves the branch zero commits behind, so the next rebase step records `UpToDate`.
- **`InputProvided`** (`$type` `input_provided`) — a person answered the questions an earlier run asked (see [AwaitingInputIssue](#awaitinginputissue)); payload-free. The answer lives on the issue's [clarification log](#clarification), which every implement prompt renders. It consumes no review comments, so the next review cutoff anchors to the run's start time and the answer comment is not read again as feedback.

Modelling the trigger as a closed union makes a half-populated revision (comments *and* failing checks, or neither) unrepresentable, and lets the `SystemPromptBuilder` switch on the kind rather than null-checking fields.
Persisted as a single `revision_trigger` JSON column and survives the outbox round-trip via `[JsonPolymorphic]` / `[JsonDerivedType]` annotations (see [ADR 0078](docs/adr/0078-revision-trigger-polymorphic-union.md)).

## Checks Fix Attempts

The number of `FailedChecks` revisions dispatched since the last human review feedback, carried on `ChecksPendingIssue` and threaded through the revision states so it survives `ChecksPending → RevisionQueued → RevisionInProgress → ChecksPending`.
`ChecksPendingIssue.FixChecks` increments it; any `ReviewFeedback` revision (`ReviewIssue.Revise` / `ChecksPendingIssue.Revise`) resets it to 0.
When a red verdict arrives that names at least one failing check and the count is below the cap (`ChecksMaxFixPushes`, = 2), the issue re-queues as a `FailedChecks` revision.
At the cap it transitions to `ReviewIssue` with `ChecksOutcome.StillFailing`.
A red verdict naming no failing check also transitions to `ReviewIssue` with `ChecksOutcome.StillFailing` and spends no attempt.

## Rebase Conflict Attempts

The number of consecutive rebase conflicts that dispatched a `RebaseConflict` revision, carried on `RebasePendingIssue` and threaded — like [Checks Fix Attempts](#checks-fix-attempts) — through `RevisionQueuedIssue`, `RevisionInProgressIssue`, `RevisionFailedIssue`, `ChecksPendingIssue` and `ReviewIssue`, so it survives `RebasePending → RevisionQueued → RevisionInProgress → RebasePending → ChecksPending → Review` (column `rebase_conflict_attempts`).
`RebasePendingIssue.ResolveRebase` applies the rebase outcome: a `Conflict` below the cap (`MaxRebaseConflictAttempts`, = 2) increments it and queues a `RebaseConflict` revision; a `Conflict` at the cap fails open to `ChecksPendingIssue` with `RebaseOutcome.Conflict`.
A successful sync (`UpToDate` or `Rebased`) resets it to 0; `Disabled` and `Skipped` leave it unchanged. Review feedback and `FailedChecks` revisions do not touch it — resetting on feedback would let two unrelated conflict episodes without a review between them exhaust the cap.
A `RebaseConflict` revision that pushes nothing still passes through `RebasePending`, conflicts again and counts toward the cap.

## Rebase Outcome

How the post-run rebase resolved, persisted on `ChecksPendingIssue` and carried onto `ReviewIssue` (like `ChecksOutcome` on `ReviewIssue`) in a single `rebase_outcome` JSON column, and exposed on the issue DTOs as `rebaseOutcome` plus `rebaseSkippedReason`. `null` for issues that passed the checks gate before the rebase step existed.
A closed union, every variant failing open to the checks gate:

- **`UpToDate`** — the branch had nothing to catch up (compare `behind_by = 0` on GitHub, `diverged_commits_count = 0` on GitLab, or the provider answered "no new commits"); no rebase.
- **`Rebased`** — the provider rebased the branch: its new head was observed, or (GitLab) the provider reported the rebase finished with no error.
- **`Disabled`** — [Rebase After Run](#rebase-after-run) was off when the step ran.
- **`Skipped(reason)`** — the rebase did not happen: `timeout`, `provider-error`, `unsupported`, `permission-denied`, `force-push-protected`, `rebase-failed`, `not-found`, `stale-head` or `unrecognised-error`.
- **`Conflict`** — the rebase conflicts with the default branch at the [conflict cap](#rebase-conflict-attempts) and fails open to the checks gate. Below the cap a conflict is never persisted as an outcome; it queues a `RebaseConflict` revision instead.

GitHub error contract (`updatePullRequestBranch` with `updateMethod: REBASE` and `expectedHeadOid`, probed 2026-11-04): every refusal is `type: UNPROCESSABLE` and is told apart only by `message` — `PullRequest::RebaseConflictError` → `Conflict`; `There are no new commits on the base branch.` → `UpToDate`; `head sha didn't match the current head ref.` → stale head (retry). `FORBIDDEN` → `Skipped(permission-denied)`, `RATE_LIMITED` → transient, and any other message → `Skipped(unrecognised-error)`. A clean rebase returns the pre-rebase `headRefOid`; the new head appears asynchronously (within ~5 s in the probe).
GitLab contract (`PUT /projects/:id/merge_requests/:iid/rebase`, polled with `GET /projects/:id/merge_requests/:iid?include_diverged_commits_count=true&include_rebase_in_progress=true`; derived from GitLab's API docs and its `RebaseService` — the live spike against a throwaway project is still to be recorded): the head is `sha` and `diverged_commits_count` is how far the branch is behind its target. A `202` (`{"rebase_in_progress": true}`) queues the rebase; there is no expected-head guard. While awaiting it: a new `sha` → `Rebased`; `rebase_in_progress: true` → stay `rebase_pending`; `rebase_in_progress: false` with `merge_error: null` → `Rebased`; with a non-null `merge_error` → `Conflict` when the merge request itself reports a conflict (`has_conflicts: true` or `detailed_merge_status: conflict`), otherwise `Skipped(rebase-failed)`. GitLab writes one generic `merge_error` for every rebase failure ("Rebase failed: Rebase locally, resolve all conflicts, then push the branch."), so the message text is never a conflict signal; a `merge_error` seen before the request is left over from an earlier rebase and is ignored. Refusals: `403 Source branch is protected from force push` → `Skipped(force-push-protected)`; `403 Cannot push to source branch` (or any other `403`, or `401`) → `Skipped(permission-denied)`; `403 Source branch does not exist` or `404` → `Skipped(not-found)`; `409 Failed to enqueue the rebase operation`, a 5xx, a `429` or a rate-limited `403` → transient, retried with the same bounded backoff as GitHub; any other status → `Skipped(unrecognised-error)`. A rebase found already running when the step starts is awaited rather than requested again.
The dashboard badges only `Skipped(reason)` and `Conflict` on the issue card; the issue detail shows every outcome.

## Claim

The act of assigning a queued issue to a specific worker run — the transition from a queued state variant to an in-progress one, paired with the `IssueClaimed` integration event that tells the Workers module to start a container.

A claim is authorized by exactly one `WorkerCapacityAvailable` event, which carries only a `WorkerRunId`.
Each event is backed by a durable [Dispatch Reservation](#dispatch-reservation) created atomically with the event; the reservation counts against slot occupancy from the moment it is written, preventing additional events from being published until the slot is consumed or released.
Before selecting a candidate, the handler checks whether any issue already carries the event's `WorkerRunId` — if so, the claim is skipped without selecting, preventing double-claims on redelivery (the at-least-once outbox guarantee means the same event may arrive more than once after a crash between claim and inbox commit).
An event that finds no candidate publishes `Claim Skipped`, which releases the reservation.

Claiming proceeds in three steps:

1. **Resolve eligible repositories** — every repository owning a queued issue is checked against Repository Eligibility, and eligible ones contribute their `Position`. Resolving all repositories up front (rather than checking the head candidate) means an ineligible repository cannot block dispatch of issues behind it.
2. **Select the winner** — the claimable issues across all eligible repositories and all three tiers are sorted by Dispatch Order key; the head of the sorted list whose repository has resolvable dispatch info wins. If the best candidate's repository cannot yield dispatch info, that candidate is skipped and the next-best is tried — the tick is NOT aborted. Dispatch info resolution is memoized per repository within a tick, so a second candidate from the same repository reuses the cached result without a second query.
3. **Claim it** — call `Claim(workerRunId)` on the aggregate, publish `IssueClaimed` carrying a `ClaimedIssueDispatch` (assembled from the winning candidate's pre-fetched dispatch info and the aggregate's own `DispatchBranchName` and `Context`), and transition the aggregate. The event and the state change commit as one atomic unit.

Claiming is the only path from a queued state to an in-progress one; nothing else in the system claims an issue. Claims are never concurrent — the outbox relay is a single host-level service that delivers sequentially.

**Terminal outcomes** are logged once at the handler: `Debug` when no eligible repositories exist or no candidates remain after eligibility filtering, `Warning` when every candidate was skipped because its repository's dispatch info could not be resolved (`AllCandidatesUnresolvable`). The "no covering credential" race is narrow — ineligibility already filters out repositories with no account in step 1, so an unresolvable dispatch info arises only when a credential is deleted or its token cleared between the last poll cycle and the claim.

## Dispatch Reservation

A durable record that holds a dispatch slot from the moment `WorkerCapacityAvailable` is published until the slot is consumed by a successful claim or released by a skip or sweep.
Stored in `dispatch_reservations` as an aggregate with `Id = WorkerRunId` and `ReservedAt`.
Created atomically with the `WorkerCapacityAvailable` outbox row inside `WorkerDispatchService`; the two writes share one transaction so the reservation can never exist without its corresponding event, and vice versa.

**Lifecycle:**

- **Reserve** — `WorkerDispatchService` creates one `DispatchReservation` per `WorkerCapacityAvailable` event it publishes.
- **Consume** — `IssueClaimedHandler` deletes the reservation for the claimed `WorkerRunId` in the same save as the new `StartingRun` row — one atomic transaction.
- **Release on skip** — `ClaimSkippedHandler` deletes the reservation when the Issues module publishes `Claim Skipped` (no eligible candidate found).
- **Release on sweep** — `StaleReservationService` deletes reservations whose `ReservedAt` is older than the stale threshold (2 minutes).

The sweep runs independently of `StaleStartingRunService` and has no Docker dependency, so a Docker outage cannot prevent stale reservations from being cleared (see [ADR 0069](docs/adr/0069-dispatch-capacity-held-by-a-durable-slot-reservation.md)).

A reservation counts against [Worker Slot Occupancy](#worker-slot-occupancy) from the moment it is persisted, so the dispatch gate sees a full slot even before the worker container starts.

## Claim Skipped

An integration event published by the Issues module (`WorkerCapacityAvailableHandler`) when a `WorkerCapacityAvailable` event is delivered but no eligible candidate can be claimed.
Carries `WorkerRunId` — the same id that was reserved and is now free to reuse.

Three non-`Selected` outcomes trigger `Claim Skipped`:

- `NoEligibleRepositories` — no repository that owns a queued issue passes eligibility checks.
- `NoCandidates` — repositories are eligible but no queued issue exists among them.
- `AllCandidatesUnresolvable` — every candidate's repository lacks resolvable dispatch info.

`ClaimSkippedHandler` (Workers module) deletes the matching `DispatchReservation` on receipt, freeing the slot for the next dispatch tick.
Redelivery is idempotent — if the reservation was already deleted (by the sweep or a prior delivery), the handler is a no-op.

## Queued Issue

The abstract intermediate in the Issue hierarchy covering all four queued state variants: `FreshQueuedIssue`, `TriageQueuedIssue`, `RevisionQueuedIssue`, and `ContinuationQueuedIssue`.
Defined as `abstract QueuedIssue : Issue`, it carries the members shared by every claimable state:

- **`TierRank`** (abstract, computed) — the dispatch-priority rank, overridden by each concrete variant.
- **`DispatchBranchName`** (abstract, computed) — the branch name the worker should operate on, or `null` for a triage run, which gets none.
- **`Context`** (abstract, computed) — the `DispatchContext` union value describing the nature of the work.
- **`Claim(Guid workerRunId)`** (abstract) — transitions the aggregate to its in-progress state with covariant return type on each override.

`QueuedIssue` collapses every three-way type union in the codebase — `is QueuedIssue` and `OfType<QueuedIssue>()` replace the former per-tier switches in `IsRestingState`, `IsQueuedVariant`, `GetUntrackableIssueNumbersAsync`, and `DispatchOrderKey.For`.

**EF Core registration.** EF Core 10 omits an abstract intermediate from the model unless it is explicitly registered — otherwise `OfType<QueuedIssue>()` and `is QueuedIssue` in translated queries throw `InvalidOperationException` at query time (not at model build or compile time).
`QueuedIssue` is registered via a dedicated `IEntityTypeConfiguration<QueuedIssue>` with `HasBaseType<Issue>()`.
No `HasValue<T>()` discriminator entry is added — EF assigns an unused default and `HasDiscriminator(...).IsComplete(true)` remains valid through the concrete leaves.
Computed get-only members (`TierRank`, `DispatchBranchName`, `Context`) are not mapped and require no `Ignore()`.

`OrderBy(TierRank)` does not translate to SQL — it refers to a computed, unmapped property. Sorting over the bounded queued set stays in memory (see [ADR 0025](docs/adr/0025-shared-dispatch-order.md)).

## Repository Eligibility

Whether a Monitored Repository meets Foundry's processing preconditions (Branch Protection and write permission).
Modeled as a value object with three variants: `Eligible`, `Ineligible` (carries a non-empty collection of `EligibilityViolation` values), and `Unreachable` (an eligibility input could not be established).
The write-probe sizing constraint (see [Provider Rate Budget](#provider-rate-budget) and [ADR 0054](docs/adr/0054-split-eligibility-cadence.md)) limits the number of repositories that can self-heal concurrently within the GitHub secondary rate limit.
`Unreachable` carries an `UnreachableReason` that distinguishes three causes — each with a distinct operator-facing message:

- **`NeverProbed`** — the write-probe result is `Unknown(Transport)`: no probe has succeeded yet, or the probe failed for a non-rate-limit reason.
- **`RateLimited`** — the write-probe result is `Unknown(RateLimited)`: the probe returned a GitHub 403 classified as rate-limit exhaustion (see below). The repository self-heals automatically when the REST budget resets; no operator action is needed.
- **`BranchRulesUnavailable`** — the branch-rules GET failed on the current poll cycle.

The `NeverProbed` and `RateLimited` causes recover via the `Unknown` self-heal cadence — the repository is automatically re-probed on the next poll cycle once a 15-minute cooldown has elapsed since the last attempt. `BranchRulesUnavailable` is retried on the next poll cycle unconditionally. `Granted` and `Denied` verdicts remain event-triggered.
Stored on the Monitored Repository and composed from two checks of different cadence:

- **Branch-rules GET (per-cycle)** — re-evaluated unconditionally on every poll cycle and synchronously at repository creation. A configuration change on the provider is reflected on the next poll without user action (auto-heal). A failed GET produces `Unreachable(BranchRulesUnavailable)`.
- **Write probe (event-triggered for Granted/Denied; self-healing for Unknown)** — runs on repository add, manual re-check, and credential update/rotation. The last result is persisted on `MonitoredRepository` as a `WriteProbeVerdict` value object (`Granted` / `Denied` / `Unknown`), which carries an `UnknownReason` (`Transport` or `RateLimited`). The verdict is composed with the fresh branch-rules result each cycle. `Unknown(Transport)` maps to `Unreachable(NeverProbed)`; `Unknown(RateLimited)` maps to `Unreachable(RateLimited)`. Both are automatically re-probed after a 15-minute cooldown; `Granted` and `Denied` are only re-probed by operator or credential events. A failed probe stamps `Unknown.LastAttemptedAt` so the next automatic retry is one cooldown away. See [ADR 0054](docs/adr/0054-split-eligibility-cadence.md) and [ADR 0055](docs/adr/0055-distinguish-unreachable-eligibility-reason.md).

**GitHub 403 rate-limit classification.** A GitHub write-probe 403 is not always a missing-permission denial.
Classification keys on explicit rate-limit response headers: a 403 with `X-RateLimit-Remaining: 0` or a present `Retry-After` header is treated as rate-limit exhaustion and produces `Result.Fail(RateLimitExhausted)` → `Unknown(RateLimited)`.
A 403 with headroom or no rate-limit headers stays `Missing` → `Denied` → `CannotPush` (fail-closed for genuine permission denial).
See ADR 0055 and [ADR 0040](docs/adr/0040-probe-403-classification.md).

A manual "re-check" action forces immediate re-evaluation of both checks.
Only `Eligible` repositories have their queued issues dispatched — ineligibility gates dispatch only; detection, dependency reconciliation, and review polling continue regardless. Already-running workers are unaffected.
Issue-level ineligibility is derived from the repository for display, never stored per issue.

## Eligibility Violation

A value object describing a specific, user-actionable eligibility precondition failure on a repository — either an access failure (`no-credential:<namespace>`, `cannot-push:<slug>`) or a Branch Protection failure (`branch-protection:*`).
An `Ineligible` result carries either a single access violation or one or more Branch Protection violations, never a mix — access is checked first and short-circuits the branch-rules evaluation.
Carries `Rule` (a well-known string constant, e.g. `"branch-protection:allow-direct-pushes"`), persisted as the sole state.
`Description` (human-readable explanation for dashboard display) is **derived** from `Rule` (and the repository's provider type, for access-related rules such as `cannot-push`) at read time in `RepositoryMappings.ToEligibilityInfo` — it is not persisted.
Legacy rows that stored a `Description` field deserialize without throwing (the field is accepted but ignored); the derived value is always used for display.
Stored as a non-empty collection on the `Ineligible` variant of Repository Eligibility, surfaced on the repository card in settings.
Provider-unreachable is modeled as the separate `Unreachable` eligibility variant rather than a violation, keeping the violation list strictly actionable.

## Issue Dependency

A directed "blocked by" relationship between two issues within the same repository.
Stored as a collection of blocking issue numbers on the Issue aggregate — the blocking issue may or may not be tracked by Foundry.
Both GitHub (REST API v2026-03-10) and GitLab (Issue Links API, Premium+) expose dependencies via structured APIs.
Foundry fetches dependencies during the detection poll cycle and reconciles them on each subsequent poll.
Because the poll can lag behind the provider (GitLab sends no webhook when an issue link is created), and a new issue is queued for triage on creation before its first dependency pass, Foundry also re-reads a `FreshQueuedIssue`'s or `TriageQueuedIssue`'s open blockers live from the provider immediately before claiming it, so an issue with an open blocker is never dispatched regardless of webhook delivery.
A claim-time hit is applied exactly as the poll path applies detected dependencies — the issue transitions to `BlockedIssue` (or `TriageBlockedIssue` for a triage candidate) and the next candidate in dispatch order is tried within the same capacity event; when every candidate is blocked or unreadable, the claim is skipped and the capacity reservation is released.
A failed claim-time read skips the candidate for that dispatch without changing its state; the next dispatch tick retries it.
`RevisionQueuedIssue` and `ContinuationQueuedIssue` are not re-checked — they run on an existing branch or pull request.
A blocker added after an issue is claimed is not detected; add blockers before applying the trigger label.
When the provider does not expose dependency links (e.g. GitLab Free tier), the provider degrades gracefully — it treats the issue as having no dependencies and logs once, rather than failing the poll.
Circular dependencies are detected by a domain service and flagged via a `CircularDependencyDetected` domain event.
A cycle does not exempt an issue from blocking: a `FreshQueuedIssue` in a cycle still transitions to `BlockedIssue`, an untriaged one to `TriageBlockedIssue`, and the event is still raised. A running or failed triage, or an issue awaiting input, only records its blockers; they decide where a ready verdict sends it.
The cycle is resolved by breaking it in the provider, after which the dependency pass unblocks the issue.
A blocker is *resolved* when it is closed in the provider — the provider client filters out closed blockers at the anti-corruption boundary so domain logic never sees them.
The provider client also scopes "blocked by" links to the same repository or project at this boundary — GitHub by `repository.full_name`, GitLab by the linked issue's numeric `project_id` — so cross-project and cross-repository links never reach domain logic.
Link entries whose project or repository cannot be determined are kept as blockers (fail-safe toward blocking).
When a blocker's state is missing or unrecognized, it is treated as still blocking (fail-safe).

## Blocked Issue

A lifecycle state for a triaged issue that has unresolved dependencies (an untriaged one waits as a [TriageBlockedIssue](#triageblockedissue)).
A triaged issue (a ready verdict or Skip triage) with open blockers transitions to `BlockedIssue` instead of `FreshQueuedIssue`.
A `FreshQueuedIssue` that gains blockers is demoted to `BlockedIssue` — whether the blockers are found by the dependency pass or by the claim-time re-check (see Issue Dependency).
A `FreshQueuedIssue` whose blockers form a cycle is blocked like any other; `CircularDependencyDetected` is raised alongside the transition.
When all blockers are resolved — that is, closed in the provider, or the link removed (including one that broke a cycle) — a `BlockedIssue` transitions to `FreshQueuedIssue` on the next dependency pass, without being triaged again. [Re-triage](#re-triage) sends it back to triage, where it waits as a `TriageBlockedIssue` while its blockers are open and is triaged once they close.

## Blocker Reason

The one-sentence reason a triage run gives for each open issue it names in a `blocked` verdict (`TriageBlocker.Reason`, never blank, at most `TriageVerdict.Blocked.ReasonMaxLength` characters).
It is shown in the posted comment and next to the blocker on the dashboard.

## Rebase Pending Issue

A lifecycle state (`rebase_pending`, Active / In flow) for an issue whose worker run left an open pull request: Foundry is rebasing the worker branch onto the default branch through the provider before the checks gate ([ADR 0087](docs/adr/0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md)). The agent never force-pushes: when this rebase conflicts, a `RebaseConflict` revision rebases the branch itself and the [Publisher](#publisher) pushes the rebased tip with a lease ([ADR 0101](docs/adr/0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md)).
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, `EnteredRebasePendingAt`, `FeedbackCutoffAt`, `ChecksFixAttempts` and [`RebaseConflictAttempts`](#rebase-conflict-attempts) — all non-nullable.
Entered from `InProgressIssue.MarkRebasePending` (a run that completed with `MergeState.Open`) and from `RevisionInProgressIssue.MarkRebasePending` / `MarkUnchangedRebasePending` (a revision that reported a branch and PR, or pushed nothing — the branch may still be behind). A merged PR or no PR keeps the existing `Completed` / `Unchanged` transitions, with no rebase.
Entering raises `IssueEnteredRebasePending`, which nudges Monitoring's rebase step; the scheduled poll is its backstop. The step's progress (pre-rebase head, request time, transient-failure backoff) is persisted per episode in `pending_rebases`, so a host restart resumes it.
The step: if `RebaseAfterRun` is off → `Disabled`; otherwise it reads the PR head and compares it with the base (`behind_by = 0` → `UpToDate`, no rebase call); otherwise it asks the provider to rebase, guarded by the head it read, and the issue stays `rebase_pending` until the PR head differs from that pre-rebase head (`Rebased`), or (GitLab) the provider reports the rebase finished, or 5 minutes pass (`Skipped(timeout)`). A stale head (a push landed in between) re-reads the head and retries, up to 3 times (`Skipped(stale-head)`). Transient provider failures (5xx, timeouts, rate limits) back off exponentially (30 s doubling, capped at 10 min) driven by nudges and the backstop poll; after 5 the outcome is `Skipped(provider-error)`.
Transitions: `ResolveRebase(RebaseOutcome)` → `RevisionQueuedIssue` with a `RebaseConflict` trigger (a `Conflict` below the [conflict cap](#rebase-conflict-attempts); raises `IssueRevisionQueued`) or else `MarkChecksPending(RebaseOutcome)` → `ChecksPendingIssue` (raises `IssueChecksPending`, so checks nudges start only after the rebase landed); `Complete()` → `CompletedIssue` (issue closed) and `Fail()` → `ContinuableFailedIssue` (PR closed without merge) — the same exits `ChecksPendingIssue` takes. A checks nudge never applies a verdict to a rebase-pending issue.

## Review Issue

A lifecycle state for an issue whose worker completed successfully and produced a PR.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, and `FeedbackCutoffAt` — all non-nullable.
Awaits human review of the PR. The monitoring service polls the provider for PR/issue status and review feedback.
`FeedbackCutoffAt` filters stale feedback — only review comments created after this timestamp are considered actionable. Tracks consumed feedback: set to the worker run's start time on first entry to review; updated to the newest consumed comment's timestamp on re-entry after a revision cycle. In steady state the cutoff never uses the worker run's completion time.
Transitions: `Revise()` → `RevisionQueuedIssue` (feedback detected); `Complete()` → `CompletedIssue` (issue closed); `Fail()` → `ContinuableFailedIssue` (PR closed without merge — branch exists).

## Unchanged Issue

A lifecycle state for an issue whose worker completed successfully (exit code 0) but produced no code changes — no branch, no PR.
Carries `WorkerRunId` — non-nullable.
Requires manual resolution: the user can retry (disagreeing with the worker's assessment).
Classified as Active / Needs you (see [Issue Lifecycle Partition](#issue-lifecycle-partition)) — it cannot resolve itself.
Transitions: `UnchangedIssue.Retry()` → `FreshQueuedIssue`.
Hard-deleted when the provider-side issue is closed or loses its trigger label (untracked by the poller). If reopened upstream with the trigger label it is re-detected as a new issue.

## Fresh Queued Issue

The first-time dispatch tier — the lowest-priority of the four queued variants (`TierRank = 3`).
An issue reaches this state from four paths: a ready triage verdict or Skip triage with no open blockers, an unblocked `BlockedIssue` (all blockers resolved; it is not triaged again), or a retried `FailedIssue` / `UnchangedIssue`.
`DispatchBranchName` is generated fresh from the kind classification, number, and title (`BranchName.Generate(KindClassification, IssueNumber, Title)`; an `Unclassified` issue gets `feat/`).
`Context` is `DispatchContext.Fresh(branchName)` — instructs the worker to start a new implementation on a new branch.
Transitions: `Claim()` → `InProgressIssue` (worker assigned); `Block()` → `BlockedIssue` (blockers detected); `Retriage()` → `TriageQueuedIssue` ([Re-triage](#re-triage)).

## Revision Queued Issue

A lifecycle state for an issue queued for revision after receiving review feedback.
Carries `BranchName`, `PullRequestUrl`, and `ReviewComments` (`IReadOnlyList<ReviewComment>`) — all non-nullable.
Created from `ReviewIssue.Revise()` when the monitoring service detects actionable review feedback.
Claimed with priority over regular `FreshQueuedIssue` to minimize open issue count.
Transitions: `Claim()` → `RevisionInProgressIssue`.

## Revision In-Progress Issue

A lifecycle state for an issue whose worker is executing a revision cycle.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, and `ReviewComments` (`IReadOnlyList<ReviewComment>`) — all non-nullable.
Created from `RevisionQueuedIssue.Claim()`.
Transitions: `MarkInReview()` → `ReviewIssue` (worker pushed changes); `MarkUnchanged()` → `ReviewIssue` (worker made no changes but PR still exists); `MarkFailed()` → `RevisionFailedIssue`.

## Completed Issue

Terminal lifecycle state — the issue is resolved via a merged PR.
Carries `BranchName`, `PullRequestUrl`, and `CompletedAt` — all non-nullable.
Created two ways:

- **Provider issue closed** (`ProviderIssueClosed`) — from `ReviewIssue`, `ChecksPendingIssue`, `RebasePendingIssue`, or `AwaitingInputIssue` via each state's `Complete()`.
- **Pull request already merged when the run ends** — from `InProgressIssue.MarkCompleted()`, when the worker outcome resolver reads the run's merge request as merged ([ADR 0060](docs/adr/0060-mr-state-first-outcome-resolution.md)).

Preserved when the issue is subsequently untracked (completion wins over provider closure).

## Failed Issue

A lifecycle state for an issue whose fresh worker run failed without producing recoverable work (no branch pushed).
Also used when a PR is closed without merge and the review had no prior branch context.
Carries `WorkerRunId`, `FailureReason` (string description), `FailureCategory`, and `FailedAt`.
Can come from `InProgressIssue` (worker failed before pushing a branch) or `ReviewIssue` (PR closed without merge, no branch recovery needed).
Transitions: `FailedIssue.Retry()` → `FreshQueuedIssue` (fresh run, no branch context).

## Continuable Failed Issue

A lifecycle state for an issue whose worker run failed but left recoverable work on a pushed branch.
Carries `WorkerRunId`, `BranchName`, `FailureReason`, `FailureCategory`, and `FailedAt` — all non-nullable.
Optionally carries `PullRequestUrl` — present when created from `ReviewIssue.Fail()` (PR was closed without merge), absent when created from `InProgressIssue` (no PR existed).
Created from `InProgressIssue` when the failed run left commits on the branch — Foundry checks via `GetBranchCommitSummaryAsync` against the provider after the container exits; commit count > 0 is the branch-has-commits boolean.
Also created from `ReviewIssue.Fail()` since `ReviewIssue` always has a branch.
Retry dispatches a continuation run that checks out the existing branch and resumes implementation.
Transitions: `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch); `ContinuableFailedIssue.RetryFresh()` → `FreshQueuedIssue` (discards the branch work — see Operator-Triggered Fresh Retry).

## Continuation Queued Issue

A lifecycle state for an issue queued for continuation from an existing branch with prior work.
Carries `BranchName` and `FailureReason` — both non-nullable. `FailureReason` is copied from the originating `ContinuableFailedIssue` and truncated to 500 characters by the aggregate.
Created from `ContinuableFailedIssue.Retry()`.
Transitions: `Claim()` → `InProgressIssue` (reuses the existing in-progress variant; the continuation context lives in the dispatch payload).

## Continuation Context

The dispatch payload extension for continuation-aware worker execution.
Carries `BranchName` and optional `FailureReason`.
Present on `ClaimedIssueDispatch` when the claimed issue was a `ContinuationQueuedIssue`; absent for fresh attempts.
When `FailureReason` is set, it is surfaced (fenced as data) in the continuation section of the worker's system prompt so the worker has context on why the previous run failed.
Semantically distinct from `RevisionContext` — continuation resumes interrupted implementation, revision addresses review feedback on a completed PR.

## Revision Failed Issue

A lifecycle state for an issue whose revision worker run failed.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, `ReviewComments` (`IReadOnlyList<ReviewComment>`), `FailureReason`, `FailureCategory`, and `FailedAt` — all non-nullable.
Created from `RevisionInProgressIssue.MarkFailed()`.
Preserves branch context so retry re-enters the revision path.
Transitions: `Retry()` → `RevisionQueuedIssue` (re-enters revision path with existing branch), by an operator or automatically: a failure whose category is auto-retryable (`transient_api_error`, `provider_unreachable`) is retried by [`TransientRetryService`](#transient-retry) on that category's schedule. Any other category waits for an operator.

## Operator-Triggered Retry

A manual action available on any retry-supporting issue via `POST /api/issues/{id}/retry`.
Dispatches polymorphically on the loaded issue state: `FailedIssue.Retry()` → `FreshQueuedIssue` (fresh run); `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch); `RevisionFailedIssue.Retry()` → `RevisionQueuedIssue` (re-enters revision path); `UnchangedIssue.Retry()` → `FreshQueuedIssue` (fresh run, operator disagrees with worker assessment).
Any non-retryable state returns a conflict error with no state change.

## Operator-Triggered Fresh Retry

A manual action for a `ContinuableFailedIssue` whose branch went in the wrong direction, via `POST /api/issues/{id}/retry-fresh`.
The handler first deletes the issue's `BranchName` on the provider (`IWorkBranchCommands.DeleteBranchAsync`), then transitions `ContinuableFailedIssue.RetryFresh()` → `FreshQueuedIssue`.
Deleting first matters: dispatch pre-creates the work branch from the default branch and reuses a branch that already exists, so a surviving branch would hand the "fresh" worker the previous commits.
An already-absent branch counts as deleted; the provider refuses to delete the repository's default branch.
If the delete fails the issue stays `continuable_failed` and the endpoint returns 502 — the operator can still use the regular Retry.
On GitHub, deleting the head branch also closes any open pull request for it.
Any other state returns a conflict error with no state change and no provider call.

## Transient Retry

A bounded, automatic retry for issues whose latest failure has an auto-retryable `FailureCategory`, run by `TransientRetryService` (a periodic background service in the Issues module, ticking at the shortest first backoff of any schedule — 1 minute).
Each category owns its schedule (`FailureCategory.AutomaticRetry`, an `AutomaticRetrySchedule`): a maximum streak of consecutive auto-retryable runs and one backoff per automatic retry. A category without a schedule never retries automatically; `FailureCategory.AutoRetryable` is exactly the categories with one, and nothing else defines a retry count or backoff.

| Category | Maximum streak | Backoffs |
|---|---|---|
| `transient_api_error` | 2 | 1 min, 1 min |
| `provider_unreachable` | 5 | 2, 4, 8, 16, 32 min |

Each tick, it loads `FailedIssue`, `ContinuableFailedIssue`, `TriageFailedIssue` and `RevisionFailedIssue` candidates (each implements `IAutoRetryableFailedState`) whose `failure_category` is in `FailureCategory.AutoRetryable` (translated to SQL `IN (…)`), with a coarse `FailedAt <= now - shortest first backoff` prefilter, then decides per candidate in memory against the schedule of the issue's **current** category (the latest failure): the streak is the number of leading consecutive failed runs whose category is auto-retryable, of any such category, derived from the append-only `worker_runs` rows (`IWorkerRunQueries.CountConsecutiveTransientRunsAsync`, bounded at maximum + 1 materialized rows); the streak breaks on the first run that is not one (a completed run, an active run, or any other failure). The issue is exhausted when the streak reaches the maximum, and otherwise due when `FailedAt + BackoffAfter(streak)` has elapsed — the backoff of the next retry, the streak's n-th backoff.
Because the latest category's maximum applies to the whole streak, a mixed streak stops on the stricter schedule when a `transient_api_error` follows an unreachable provider (U, E: 2 ≥ 2) and keeps going when an unreachable provider follows an API fault (E, U: 2 < 5). The streak can never exceed the largest maximum, so the loop stays bounded.
Due candidates are re-queued through the existing transition — `FailedIssue.Retry()` → `FreshQueuedIssue`, `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue`, `TriageFailedIssue.RetryTriage()` → `TriageQueuedIssue`, `RevisionFailedIssue.Retry()` → `RevisionQueuedIssue` — after reloading the issue, so a de-labelled or already-retried issue is a safe no-op (a concurrent manual retry that moved the issue out of the failed state, or won the `DbUpdateConcurrencyException` race, is tolerated).
Schedules are hardcoded (no settings surface). An exhausted issue stays failed, leaving `POST /api/issues/{id}/retry` as the manual escape hatch. The issue detail's "Attempt n of N" block (`TransientRetryDetails`) applies to the same four states and categories, with N the current category's maximum.
Due-ness is recomputed from the persisted `FailedAt` on every tick, so a host restart during a backoff window costs at most one extra tick of delay — no in-memory timer is required.

## Review Comment

A value object representing a single piece of reviewer feedback on a PR.
Carries `Body` (text), optional `FilePath`, and optional `Line` number.
Lives in Issues contracts — produced by the monitoring provider, consumed by the worker dispatch.

## Review Feedback

A provider-agnostic result from `IIssueProvider.GetReviewFeedbackAsync()`.
Carries an `IReadOnlyList<ReviewComment>`, an `OmittedCommentCount`, and the `NewestCommentAt` timestamp of the newest kept comment.
The actionability rule is provider-agnostic and lives entirely in `ActionableFeedbackPolicy` (see [ADR 0070](docs/adr/0070-any-human-comment-is-actionable-feedback.md)): every human-authored, non-system comment created after `FeedbackCutoffAt` is actionable — regardless of review state, thread-resolution state, or provider.
HTTP clients (`GitHubHttpClient`, `GitLabHttpClient`) are pure mappers; they project provider JSON to `ProviderComment` lists and return all available comments without filtering.

## Actionable Feedback

A `ProviderComment` that survives all `ActionableFeedbackPolicy` filters and is included in the `ReviewFeedback` delivered to the worker.
A comment is actionable when it is not in the [posted-comment registry](#posted-comment-registry), human-authored (not a bot, not a system note), created after `FeedbackCutoffAt`, older than the quiet-period boundary (`now - 2 minutes`), and belongs to the newest 50 by `createdAt`.
The policy is provider-agnostic; provider-specific mapping concerns (body truncation, file-path sanitisation) remain in the HTTP clients.

## Conversation Comment

A PR-level (non-inline) comment posted directly on the pull request — not attached to a specific file line or review thread.
On GitHub, sourced from `pullRequest.comments`; on GitLab, sourced from discussion notes on non-resolvable discussions.
Mapped to `ProviderComment` with `Origin = CommentOrigin.Conversation`.

## Provider Comment

The internal raw mapping type (`ProviderComment`) produced by each HTTP client before policy filtering.
Carries `Id` (the provider comment ID; absent only when GitHub omits `databaseId`), `Body`, `AuthorLogin`, `AuthorIsBot`, `IsSystem`, `CreatedAt`, `FilePath`, `Line`, `Origin`, and `ThreadResolved`.
Never crosses a module boundary — only the already-public `ReviewComment` does.

## Review Summary

A PR review object with a non-empty body, independent of its approval state.
On GitHub, sourced from `pullRequest.reviews { databaseId body submittedAt }` with no state filter — `APPROVED`, `COMMENTED`, and `CHANGES_REQUESTED` reviews are all eligible when they carry a body.
Mapped to `ProviderComment` with `Origin = CommentOrigin.ReviewSummary`; `submittedAt` is used as `CreatedAt`.

## Posted-Comment Registry

The IDs of comments Foundry posted through the comment-post operation (`IIssueProvider.PostCommentAsync`), stored per monitored repository in `posted_comments` and keyed by repository and provider comment ID.
Every comment reader excludes a registered comment **by ID rather than by author**: Foundry posts with the account PAT, so excluding by author would also drop the PAT owner's own replies, and excluding by a body marker would drop a human quoting the comment.
`ActionableFeedbackPolicy` and the issue-thread reader (`GetIssueFeedbackAsync`) both take the repository's `PostedCommentIds` snapshot, loaded through `PostedCommentRegistry`.
Matching applies to conversation comments only — the only surface Foundry posts to — because GitHub numbers reviews and review comments independently of issue comments.
The caller of the comment-post operation records the returned ID with `PostedCommentRegistry.Record`, in the same save as the state change that tracks the post where it can; if that save fails, the comment can be read as feedback at most once.
The blocker comment is one such registered post: after linking the blockers a `blocked` triage verdict names, `TriageBlockersFoundHandler` posts a single issue-thread comment through `BlockerLinkWriter.PostIssueCommentAsync`, which registers the returned ID at once, so the comment is never read back as feedback. It lists each blocker with its [Blocker Reason](#blocker-reason) in a code span.

## Comment Post

`IIssueProvider.PostCommentAsync` posts a body to a pull/merge request conversation or an issue thread (`CommentThread`) using the account PAT and returns the provider's comment ID (`ProviderCommentId`).
A failure is either `CommentPost.Transient` (5xx, 408, 429, a rate-limited 403, a timeout or connection failure) or `CommentPost.Permanent` (any other 4xx, a blank body, a thread URL without a number); callers retry only transient failures and own the retry policy.

## Issue-Thread Reader

`IIssueProvider.GetIssueFeedbackAsync` returns an issue's own comments created after a cutoff, oldest first, minus the posted-comment registry — on GitHub from the issue-comments REST endpoint, on GitLab from the issue notes endpoint.
Each `ProviderComment` carries its ID, author, body, creation time and bot/system flags; bot and system comments are kept and flagged so callers decide (GitLab flags system notes, and project/group access-token bot users by their generated username).
Pull/merge request threads keep using `GetReviewFeedbackAsync`.

## Revision Context

The dispatch payload extension for revision-aware worker execution.
Carries `BranchName`, `PullRequestUrl`, and `IReadOnlyList<ReviewComment>`.
Present on `ClaimedIssueDispatch` when the claimed issue was a `RevisionQueuedIssue`; absent for fresh attempts.
The worker uses this to check out the existing branch and address the specific review comments.

## System Prompt Budget

The assembled `SYSTEM_PROMPT` is delivered to the worker container as a single environment variable and as a single `--append-system-prompt` argv. Linux caps any single argv or env string at `MAX_ARG_STRLEN` = 131,071 bytes, so an oversized prompt fails at `execve` with an opaque `E2BIG` before `claude` starts (see [ADR 0071](docs/adr/0071-bound-assembled-system-prompt-under-max-arg-strlen.md)).

`SystemPromptBuilder` bounds the fully assembled prompt to a design ceiling of **120,000 UTF-8 bytes**, measured after XML encoding and after newline normalisation (`\r\n` → `\n`) — i.e. on exactly the bytes delivered, never on character counts. `entrypoint.sh` guards defensively at **131,071 bytes** (the kernel `MAX_ARG_STRLEN`) as a last line of defence against drift or a caller that bypasses the builder. The two numbers are a coupled pair and must move together: the C# ceiling leaves headroom below the kernel limit, and the shell guard only ever fires on drift.

Fixed contributors — the safety preamble, the Worker Principles, the base template, and the section scaffolding — are assembled first and are non-negotiable. Only the variable revision-comment list consumes the remaining quota:

- Comments over budget are dropped **oldest-first**, and the prompt states the size-omission count distinctly from the upstream 50-cap omission count (`OmittedCommentCount`), so the worker knows feedback was withheld rather than silently receiving a partial set.
- A single comment larger than the whole remaining budget is **truncated on a UTF-8 character boundary** and marked, never dropped — the feedback section is never rendered empty, and a multi-byte sequence or surrogate pair is never split.
- When the fixed floor alone exceeds the budget (e.g. a role ships a very large system prompt), the dispatch **fails explicitly** with a `Worker.SystemPromptTooLarge` error rather than truncating the safety rules or overflowing at `execve`.

## Trigger Label

The constant label `foundry` applied to a provider-side issue to flag it for Foundry processing.
The label stays on the issue untouched — Foundry does not add, remove, or swap labels on the provider.
All lifecycle state is tracked internally in the database.

## Provider as Source of Truth for Issue Closure

The provider is the authoritative signal for issue closure.
When an issue's trigger label is removed or the issue is closed on the provider, it disappears from the `?labels=foundry&state=open` fetch.
On each poll cycle, the poller emits a `ProviderIssueUntracked` integration event for any tracked issue absent from that fetch — but only when that fetch is provably complete.
The provider clients stop paginating at a fixed page cap and return `IssueListing.IsComplete: false` when the cap is reached with pages still outstanding.
An incomplete listing cannot distinguish an issue closed on the provider from one that simply fell outside the fetched window, so the untrack pass is skipped entirely and the repository enters **untrack suppression**, logged once on entry rather than every cycle.
Detection, detail-change, dependency, and review passes all continue normally and the poll still succeeds — only deletion is withheld.
Suppression clears automatically on the first later cycle whose listing is complete; no operator action is involved.
The `ProviderIssueUntrackedHandler` hard-deletes tracked records in resting states (`Issue.IsRestingState()`): `detected`, `triage_queued`, `queued`, `revision_queued`, `continuation_queued`, `blocked`, `triage_blocked`, `failed`, `triage_failed`, `continuable_failed`, `revision_failed`, `branch_diverged`, and `unchanged`.
`completed` is preserved — completion wins over provider closure.
`triage_in_progress`, `in_progress`, and `revision_in_progress` are preserved — a live worker is running. Untracking never stops a worker; stopping one is an explicit user action (see Run Cancellation).
`review`, `checks_pending`, `rebase_pending`, and `awaiting_input` are preserved — each asks the provider whether the issue is closed (`ProviderIssueClosed`) and completes when it is.
`publish_failed` and `publish_blocked` are preserved — their run's unpublished work waits in its Hand-off volume, and the issue leaves only when the run is discarded or the volume's time to live expires.
An issue closed on the provider and later reopened with the trigger label is re-detected as a new issue.

## Provider Issue

A DTO representing an issue as returned by a provider's API.
Carries raw data (number, title, author username, URL, labels) that the domain maps into value objects and aggregates during detection.

## Repository Slug

The full namespace path that identifies a repository within a provider — two or more `/`-separated segments (`owner/name` on GitHub, `group/subgroup/project` for nested GitLab groups).
Value object — `Name` is the last segment, `Owner` is everything before it (a multi-segment namespace path for GitLab). The provider adapter renders it as needed: GitHub as `owner/name` path segments, GitLab as a URL-encoded full path.

## Worker Role

The job a worker run is dispatched to do ([ADR 0095](docs/adr/0095-worker-roles-ship-in-the-worker-image.md)): `triage` (judges whether an issue is ready to implement and classifies it) and `implement`; `review` is planned. Every new issue is dispatched to `triage` before its first `implement` run (see [TriageQueuedIssue](#triagequeuedissue)).
Each role brings its own Claude Code plugin (skills, agents), prompt and an optional verdict schema from `workers/roles/<role>/` (plus `workers/roles/shared/`, loaded for every role). These folders are baked into the worker image at `/opt/foundry/roles/`.
Foundry's code picks the role, starts the run and acts on the verdict ([ADR 0094](docs/adr/0094-code-decides-claude-judges-only-what-code-cannot.md)). A WorkerRun has exactly one role. Fresh, revision, continuation and rebase-conflict runs all use `implement`. Their context sections stay in `SystemPromptBuilder`.
"Step", "stage" and "phase" already have other meanings here, so they are not used for this concept.

- **Folder layout**: `.claude-plugin/plugin.json` (its `description` is the role's purpose), `shared/prompts/principles.md` (the Worker Principles framing line) and `shared/prompts/principles/<section>.md` (one file per Principle Section; all required, no placeholders), `prompts/system.md` (appended after Foundry's safety preamble and the role's Worker Principles; placeholders `{issueNumber}` and `{issueContent}`), `prompts/task.md` (the `claude -p` task; must contain `{issueNumber}`), optional `skills/<skill>/SKILL.md`, `agents/<agent>.md` and `verdict.schema.json`.
- **Worker Principles**: the engineering standards under `workers/roles/shared/prompts/`. The framing file `principles.md` holds one line that introduces them; the standards themselves are grouped into Principle Sections, one file each under `principles/`. Each Worker Role declares in code which sections it receives, and its system prompt carries the framing line followed by those sections, after the safety preamble and before the role prompt (implement: all four; triage: Design and Domain modelling). They are worded as properties of code, so each role applies, judges against, or designs to them as its own prompt says. Unlike the safety preamble, they yield to the target repository's style, testing and design conventions, and never to the safety rules. Every file is required and takes no placeholders. A missing or blank file, one containing a `{placeholder}`, or a framing line plus all four sections over 8 KiB (UTF-8 bytes) together fails the unit tests and the startup image build.
- **Principle Section**: a named group of Worker Principles (Testing, Design, Code, Domain modelling), held in one file at `workers/roles/shared/prompts/principles/<section>.md`; each Worker Role declares in code which sections its system prompt carries. The sections follow the shared framing line in the section type's fixed order, and a role with no sections gets neither the framing nor any principles.
- **Names come from code**: `WorkerRole.All` lists the dispatchable roles, and each role declares its capability and its Principle Sections there. A role without a folder, or a role prompt using a placeholder Foundry does not fill, fails the unit tests and the startup image build, as does a missing, blank or placeholder-bearing framing or section file, or principles over 8 KiB combined. The entrypoint receives the role as `WORKER_ROLE` and loads `/opt/foundry/roles/<role>` and `/opt/foundry/roles/shared` with `--plugin-dir`. A role this image does not carry fails bootstrap at stage `role`.
- **Role Catalog**: the startup image build reads every role's manifest, skills, agents, prompts and verdict schema, plus the shared Worker Principles framing and section files, from the same build-context tar it sends to Docker, and holds the catalog in memory (`IWorkerRoleCatalogStore`). Dispatch takes the role's prompts and its assembled Worker Principles from it, and `GET /api/worker-roles` serves it to the dashboard's role cards (read-only except the Role Model) together with the `SystemPromptBuilder` section metadata (each section in assembly order, tagged with when it is included; the role prompt and the Worker Principles stand as `{rolePrompt}` and `{principles}` slots, the others are hardcoded). Each card carries its role's selected Principle Section names and assembled principles text, and the catalog carries every section name in fixed order. With image build disabled (operator-supplied tag), the catalog is read from disk and marked unverified against the running image. A role change merged while Foundry runs takes effect at the next restart, and until then the cards keep showing what the workers actually run.
- **Capability profile** ([ADR 0098](docs/adr/0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md)): every role declares a `WorkerCapability` in code (`WorkerRole`, carried by `WorkerRoleDefinition`; never read from its folder), `Restricted` or `Full`. `implement` is `Full`, `triage` is `Restricted`. The container spec, the entrypoint (`WORKER_CAPABILITY=restricted|full`) and `SystemPromptBuilder` branch on the profile, never on the role name. A `Full` run gets the account token (`GH_TOKEN`/`GITLAB_TOKEN`), a branch pre-created on the provider, `--dangerously-skip-permissions` and the issue as an `ISSUE_API_URL` pointer. A `Restricted` run gets no provider token and no branch: it clones a base bundle holding only the default branch ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)), `claude` runs with `--restricted --strict-mcp-config` and never the permission skip, and Foundry reads the issue title, body and comments (`IIssueContentQueries`, through `IIssueProvider.GetIssueContentAsync`) into `{issueContent}` as XML-encoded untrusted data, dropping the oldest whole comments first when the prompt budget runs out. Its system prompt carries restricted safety rules and none of the implement-only sections (pre-PR, checks, fresh, revision, continuation). A failed issue read fails the run before any container starts.
- **Recorded on the run**: every WorkerRun stores its `Role` (column `role`) and the `Image` it was dispatched on (column `image`, `tag@image-id` when the image ID is known).
- **Verdict**: a role with `verdict.schema.json` returns a schema-validated verdict. The entrypoint merges the `$defs` of `workers/roles/shared/verdict.defs.json` (e.g. the shared `questions` definition) into the schema and passes it to both `claude` invocations with `--json-schema`. Foundry reads the last result event's `structured_output` and parses it with the role's typed verdict parser (`IWorkerVerdictParser`; `implement`: `{outcome: done | needs_input, questions}`; `triage`: `{outcome: ready | needs_input, kind: feature | bug | refactor | documentation, questions}`, recorded as `done` for `ready`). For a run that exits 0, the terminal run records `VerdictStatus` (column `verdict_status`: `done`, `needs_input`, `missing` or `invalid`) and, for `needs_input`, its `VerdictQuestions` (column `verdict_questions`). The verdict is recorded only: the run's outcome is still decided from facts, and nothing acts on `needs_input` yet.
- **Role Model**: the one per-role operator setting, stored in `worker_role_settings` keyed by role name (`WorkerRoleSettings`). A migration seeds `triage` with `sonnet`; the operator changes or clears it like any role model. It is empty (the CLI's account default; no `model` is written to the managed settings), a documented Claude Code alias (`fable`, `opus`, `sonnet`, `haiku`, `opus[1m]`, `sonnet[1m]`; the code constant `ClaudeModelAliases`), or a model ID listed by the Anthropic Models API. There is no global model and no fallback chain. Dispatch writes the role's model into the managed settings `model`.
- **Model List**: `IClaudeModelCatalog` fetches `GET https://api.anthropic.com/v1/models?limit=100` (paged with `after_id` while `has_more`, reading `id`, `display_name` and `lifecycle`) with the credential workers use: the API key (`x-api-key`) or the OAuth access token read from the credential volume through the credential-helper container (`Authorization: Bearer`). The list is cached in memory; `GET /api/worker-roles/models?refresh=true` refreshes it when the role view opens. A failed refresh keeps the previous list and reports the error, and the role card still offers Default, the aliases and the saved value.
- **Saving a model**: `PUT /api/worker-roles/{role}/model` accepts empty, an alias, or an ID in the current list, and otherwise rejects it with `Worker.RoleModelNotListed`. A saved model the list later drops is shown as unavailable and dispatch still uses it.
- **Model Unavailable**: the CLI validates the model only on its first request. A run whose result line reports "There's an issue with the selected model" fails with `FailureReason.ModelUnavailable` (category `model_unavailable`), whose summary names the role whose model setting to change.

## WorkerRun

A single execution of a worker against an issue — the primary aggregate for the Workers module.
Modeled as a polymorphic aggregate with state variants: `StartingRun` (container creation requested), `ActiveRun` (container running), `PublishingRun` (container ended; its commits are being published), `PublishFailedRun` (a publish attempt failed), `CompletedRun` (exited successfully), `FailedRun` (exited with error, timed out, failed to start, or was discarded).

**Publishing** ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)). A run with a branch moves `Running → Publishing → Completed` (or `Failed`): when its container exits, times out, is cancelled or is found gone, `ActiveRun.BeginPublishing` keeps the exit facts, container log tail, verdict and the verdict's raw `pullRequest` text on a `PublishingRun`, and the container is removed while the Hand-off volume stays. `RunPublishingService` then runs `WorkerRunPublisher`: the Publisher pushes the Run bundle; for a run that exited 0 without a `needs_input` verdict, code opens the pull request when none is open (title `<type>: <summary>` from the issue's kind and the verdict's validated summary, else the issue title; body the verdict's `PullRequestDescription`, else one built from the commit subjects, then `Closes #<issue>`), or posts one summary comment on the open one (recorded in the posted-comment registry; the body is never overwritten); then the outcome is resolved and applied as before. Each step is recorded (`published_at`, `pull_request_settled_at`), so a retry never pushes, opens or comments twice. `WorkerRunCompleted` carries the publish facts (`RunPublishFacts`: pushed head, pull request URL, diverged side branch and its [divergence reason](#branch-divergence-reason), branch commit count), and the run's dispatch slot is released only then.
**PublishFailed.** A transient publish failure (including `kind=unreachable`) moves the run to `PublishFailedRun`, retried after `PublishRetryPolicy.RetryDelays` (1, 2, 5, 15 minutes; `MaxAttempts` 5); while the run's host is paused ([Host Pause](#host-pause)) a due retry waits without spending an attempt. A permanent failure, or the last attempt, exhausts it: the run frees its slot, raises `WorkerRunFailed` with category `publish_failed`, and its issue waits in Needs you with **Retry publish** (`POST /api/workers/runs/{id}/publish-retry`: `202`, `409` when the run is not waiting for a publish retry, `404`), which reruns Publish from the retained Hand-off volume. A diverged run completes with its side branch and divergence reason, and its issue waits in Needs you as branch diverged.
**PublishBlocked.** A run whose publish the [Diff gate](#diff-gate) blocked raises `WorkerRunPublishBlocked` (the finding count only) and frees its slot; its issue waits as `publish_blocked`. **Discard run** (`POST /api/workers/runs/{id}/discard`: `202`, `409` when the run is not holding unpublished work that waits for an operator, `404`) ends a blocked run or an exhausted publish-failed run: the run fails with category `run_discarded`, its Hand-off volume is deleted, and its issue fails, so Retry Issue starts a new run from the branch. The Hand-off time to live discards a blocked run like a publish-failed one.
An issue can have multiple runs (e.g. after retry from Failed or Review state).
Foundry derives each run's outcome via `WorkerOutcomeResolver` — a pure function that consults merge-request state first (see Worker Outcome Detection).
`ActiveRun` carries the `BranchName` — set at activation from the dispatch payload's pre-created branch name, propagated to `FailedRun` and `CompletedRun`.
Every run state carries the publish facts (`PublishedHead`, `PublishedBranchCommitCount`, `DivergedSideBranch`, `DivergenceReason`) once it was published — see Branch Commit Count below.

**Supplied open issues.** `SuppliedOpenIssueNumbers` (column `supplied_open_issue_numbers`) records the open issues Foundry supplied to the run before the prompt was fitted to its byte budget: the open referenced issues, then the open-issue index. A number the fitting dropped is therefore still accepted, an accepted trade-off because it is a real open issue in the same repository. Foundry records it at dispatch from the full tracker context, and every transition carries it forward. A triage run's `blocked` verdict may name only these issues. `TriageVerdict.Blocked.RejectionAgainst` runs in `WorkerOutcomeResolver` and rejects an issue outside the set, a duplicate, an over-cap list or a reason over 500 characters as `FailureReason.VerdictInvalid`. A run with no recorded set (dispatched before the column existed) rejects every blocked verdict with the distinct `NoSuppliedSetDetail`, so the failure does not blame the model. A rejected `blocked` verdict is recorded as an `invalid` run verdict, not `done`. The reader collapses each reason's whitespace and control characters to single spaces before the length check, and treats a reason blank after that as a broken verdict; the consumer side is under [Triage Verdict](#triage-verdict) and [TriageInProgressIssue](#triageinprogressissue).

**`StartingRun` bounded lifetime.**
`StartingRun` is the state a worker run occupies between row creation (in `IssueClaimedHandler`) and container start (`StartAsync`).
This window exists because the row must be committed before the Docker API is called — a crash or daemon failure in that gap would otherwise leave the issue in `in_progress` with no recovery path.
`StartingRun` rows are bounded to at most `StaleStartingRunThreshold` (10 minutes) by the periodic sweep (see Orphan Reconciliation).
After that threshold, the sweep fails the run via `StartingRun.Fail(ContainerError)`, which raises `WorkerRunFailed`, which the Issues module's `WorkerRunFailedHandler` turns into `InProgressIssue → FailedIssue` — making the issue retryable from the dashboard (see [ADR 0050](docs/adr/0050-recover-stranded-starting-runs-via-the-failure-bridge.md)).

**Run Cancellation.**
A user can stop the worker of an `in_progress` or `revision_in_progress` issue with **Cancel run** on the issue detail page (behind a confirmation dialog).
`POST /api/workers/runs/{runId}/cancellation` only records the request: `ActiveRun.RequestCancel(at)` sets `CancelRequestedAt` (column `cancel_requested_at`); a repeated call keeps the first timestamp and still returns `202 Accepted` — idempotency is enforced on the aggregate.
Only `ActiveRun` has `RequestCancel`: an unknown run returns `404`, a `StartingRun`, `CompletedRun` or `FailedRun` returns `409`. Cancelling a `StartingRun` is deliberately unsupported — its window is seconds and the stale-starting sweep covers hangs.
`WorkerDispatchService` stays the single writer of run state. On its next tick, for a still-running container with a cancel request — checked **before** the timeout, so cancel beats timeout — it stops the container, captures its logs, and checks the provider for a merge request, all within one tick:
an open or merged merge request means the worker finished before the cancel took effect, so the run resolves normally (`Review` / `Completed`); otherwise `StoppedRunOutcomeBuilder` (shared with the timeout path) builds a `FailureReason.Cancelled` outcome — `ContinuableFailure` when the branch has commits or the commit lookup fails transiently, `Failure` when the branch is `NotFound` — applied through `ApplyOutcomeAsync` like every terminal outcome.
A transient merge-request lookup failure is treated as "no merge request" (accepted risk).
A container that already exited before the tick resolves to its real outcome (side effects such as a usage-limit pause stand); a `NotFound` container resolves through the normal not-found path; an `Unreachable` daemon leaves the persisted request pending until the daemon is reachable again.
`CancelRequestedAt` is carried into `CompletedRun` and `FailedRun` and exposed on the run-detail DTO (`cancelRequestedAt`), so the dashboard shows "Cancelling…" until `IssueUpdated` arrives, and tells a run that completed before the cancel took effect apart from an ordinary one.
No Issues-side handler is involved: `WorkerRunFailedHandler` already routes a cancelled `InProgressIssue` run to `ContinuableFailedIssue` (branch with commits — Retry continues from the branch) or `FailedIssue`, and a cancelled `RevisionInProgressIssue` run to `RevisionFailedIssue`. `TransientRetryService` never auto-retries a cancelled run.

## Worker Slot Occupancy

The count of dispatch slots held at any moment — the figure checked against `MaxConcurrent` before a new run is dispatched.
Slot occupancy is the union of reservations, starting, active and publishing runs, and runs retrying a publish (`reservations ∪ starting ∪ active ∪ publishing ∪ publish-retrying`): a run holds its slot until publishing finishes. A run whose publish retries are exhausted frees its slot.
Including `DispatchReservation` in the count prevents over-dispatch during the window between the `WorkerCapacityAvailable` event being published and the claim being processed; including `StartingRun` prevents over-dispatch during the window between row creation and container start.
An unresolved `DispatchReservation` holds its slot for at most `StaleReservationThreshold` (2 minutes) before `StaleReservationService` deletes it.
An unresolved `StartingRun` holds its slot for at most `StaleStartingRunThreshold` (10 minutes) before `StaleStartingRunService` fails it (see Orphan Reconciliation).
The query is implemented by `DbContext.GetSlotOccupancyCountAsync` and `DbContext.GetSlotOccupancyRunIdsAsync` extension methods, used by both `WorkerDispatchService` (dispatch gate) and `StaleStartingRunService` (orphan reaping).
The same count is exposed read-only as `GET /api/workers/capacity` (`MaxConcurrent`, `OccupiedSlots`, `FreeSlots`) for the [Floor](#floor), so the dashboard never recomputes occupancy.

This occupancy count is **distinct from the per-repository cap**: `MaxConcurrent` is a global ceiling enforced Workers-side via this count; the [Repository Worker Limit](#repository-worker-limit) is a per-repository ceiling enforced Issues-side from `count(InProgressIssue) + count(RevisionInProgressIssue)` grouped by repository.
Neither count subsumes the other — `DispatchReservation` and `StartingRun` carry no repository identity and are therefore invisible to the per-repository count; `InProgressIssue` and `RevisionInProgressIssue` are not reservation types and are not included in slot occupancy.

## Branch Commit Count

The number of commits the issue branch is ahead of the repository's default branch by merge-base comparison.
It is set from the publish facts: the Publisher reports it with every push (`FOUNDRY_PUBLISH … commits=<n>`), and it is kept on the run as `PublishedBranchCommitCount` and on `WorkerRunCompleted`. There is no live count while a run is working ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)); Agent Activity shows progress instead.

## Worker Activity Observation Loop

This loop is reached only for a run whose container the tick has already confirmed alive (`status.IsRunning`). On each ~10 s `WorkerDispatchService` tick, for every such `ActiveRun` the service:

1. Calls `ActiveRun.RecordActivity(now)` unconditionally — the container is provably running, so `LastActivityAt` advances every tick and `WorkerActivityObserved` is raised (monotonic: an out-of-order timestamp is ignored). Liveness is derived from container-alive, not from log growth — the worker buffers its stdout until exit, so log length is not a progress signal.
2. If any domain events were raised, saves to the database (within the same scope transaction) and dispatches the events.

`LastActivityAt` therefore means "last confirmed alive"; whether the agent is doing something shows in its [Agent Activity](#agent-activity).

`WorkerActivityObserved` is handled by `WorkerActivityObservedHandler`, which broadcasts a `WorkerActivity` payload (`WorkerRunId`, `IssueId`, `LastActivityAt`) to all connected dashboard clients via `WorkerHub`.

## SignalR Worker Activity Replay

`WorkerHub.OnConnectedAsync` replays a `WorkerActivity` payload for every active run to the connecting client, so a dashboard reload or reconnect renders the current last-activity time without waiting for the next observation tick.
Live pushes and connect-replay use the same `WorkerActivity` client method — the frontend makes no distinction between them.

**Dashboard display.**
Log silence is a frontend-only signal derived from the [Agent Activity](#agent-activity) `LastEventAt` — the time of the last stream-json event the agent emitted — shown only past a 5-minute threshold. While a run publishes, the card shows `Publishing commits` (or `Publish failed, retrying`) instead of the activity line.
Because `LastEventAt` advances only when the agent itself produces output (including `system/thinking_tokens`), the badge detects an agent that is alive but idle; when a tool is in flight the badge names it (e.g. `silent 6m · Bash running`) so a long-running test suite is not mistaken for a hang.
Until the first Agent Activity payload arrives (e.g. during the SignalR handshake) the card falls back to `LastActivityAt`, which only lapses when observation itself stops.

## Agent Activity

A live, in-memory projection of a running worker's stream-json events, one per `ActiveRun` — the agent's own view of its progress, read without a Foundry-defined stage vocabulary.
It holds the agent's task list (`TaskCreate` / `TaskUpdate`: subject, `activeForm`, status, with task ids correlated from the `TaskCreate` tool results), the current in-progress task and "N of M" completed, the active subagents (`subagent_type`, description, `spawn_depth`, last tool — parallel subagents tracked separately, each removed when its `task_notification` reports completion), the active skill, the in-flight tool (the latest `tool_use` with no matching `tool_result`, with the Agent Transcript input summary), token totals (input, output, cache read, cache creation — summed once per `message.id`, since a message is emitted once per content block with identical `usage`), and `LastEventAt`.
Cost is not estimated live; it arrives with the final `result` event.

**Follower.**
`AgentActivityFollowerService` (not `WorkerDispatchService`) reconciles a log follower per `ActiveRun` every 10 s and flushes changed projections every 2 s.
Each follower reads the container log from the start, so a Foundry restart rebuilds the projection by re-attaching (cost proportional to run length — accepted).
A repeated `system/init` or the `claude -c` transient retry continues the projection rather than resetting it; a `result` event clears the in-flight tool and subagents but keeps tasks and token totals.
When the run leaves `ActiveRun` its follower is cancelled, its projection disposed, and an `AgentActivityEnded` payload is broadcast; a follower whose log stream faults is re-attached with a fresh projection on the next reconcile.

**Broadcast and replay.**
Changed snapshots are broadcast as an `AgentActivity` payload via the `WorkerHub` `AgentActivity` client method, and `WorkerHub.OnConnectedAsync` replays the current payload for every active run after the `WorkerActivity` replay ([ADR 0063](docs/adr/0063-replay-live-worker-state-over-signalr-instead-of-the-issue-read-model.md)).
The projection is presentation-only: it is never persisted, and text fields are redacted summaries — never tool result bodies.

**Dashboard display.**
The in-progress issue card shows one line such as `developer · Running tests (3/7)`: the latest active subagent type (or `main agent`), the current task `activeForm`, and task progress; a run with no task list shows the active subagent and the in-flight tool instead.
The worker system prompt carries one workflow-neutral line asking the agent to track its plan with its task list and keep it current.

## Floor

The dashboard's factory-floor view (`/floor`): one lane per active run showing its live [Agent Activity](#agent-activity), followed by the dispatch slots that hold no lane and the next queued issues.

**Lanes.**
Lanes come from the `WorkerHub` Agent Activity payloads, so a page reload repopulates them from the connect replay.
Each lane shows the issue number, title and repository; the task list with the current task highlighted and "N of M done"; active subagents (parallel and nested listed separately, collapsed beyond a visible limit) and the active skill; a tool ticker of the most recent in-flight tools (accumulated client-side from successive payloads, so it restarts on reload); token totals; and the time since `LastEventAt`, styled as silent past the same 5-minute threshold as the issue card.
A lane leaves the Floor when `AgentActivityEnded` arrives or its issue leaves the working states; the run's cost then appears on the issue via `RunResultSummary`.
While the worker hub is disconnected lanes show stale styling; after a reconnect, runs the replay does not re-send within a short grace window are pruned, covering an `AgentActivityEnded` missed while disconnected.

**Slots and queue.**
Free and starting slots come from the server's [Worker Slot Occupancy](#worker-slot-occupancy) via `GET /api/workers/capacity`: occupied slots without a lane (reservations, starting runs) render as *Starting*, the remaining `FreeSlots` as empty lanes.
The queue lists the next eligible queued issues in [Dispatch Order](#dispatch-order), reusing the dashboard's server-ordered issue list ([Queue Position](#queue-position), ADR 0067).

**Active-agents chip.**
The top bar shows `● N agents working` (the count of active runs reporting Agent Activity), or an idle state at zero, linking to the Floor.
Count changes are announced through the shell's single polite announcer; per-lane step changes are announced politely by the lane, and token or clock ticks are never announced.

## Worker Outcome Detection

How Foundry establishes what a worker accomplished, resolved by `WorkerOutcomeResolver` — a pure, side-effect-free function applied after the container exits (and on timeout, orphan-reconcile, and container-not-found paths).

**MR-state-first lookup.**
The resolver queries `GetMergeRequestByBranchAsync`, keyed on the run's stored `BranchName` (which survives remote branch deletion).
The resulting presence maps to outcomes:

- `Merged` → `Completed` (transitions `InProgressIssue` → `CompletedIssue`)
- `Open` → `Review` (transitions `InProgressIssue` → `ReviewIssue`)
- `Closed` (unmerged) + branch has commits → `ContinuableFailure`; otherwise → `Failure`
- `None` (no MR found) → fall back to exit-code + branch-commits path (see below)

The "branch has commits" boolean is derived from `GetBranchCommitSummaryAsync` (commit count > 0) — the single consolidated query behind both the closed-MR and no-MR paths.

**NotFound vs transient errors.**
Provider query failures carry `Error.Kind`.
Only `ErrorKind.NotFound` is a definitive signal — "branch deleted, no commits reachable".
Any other failure (`Unknown`, network error, timeout) yields `Indeterminate` — no state transition, the run stays active and is retried on the next tick.

**No-MR fallback.**
When no MR exists the resolver falls back to exit code and branch commits.
Exit 0 with commits triggers a bounded MR re-poll (preserving the PR-race retry window); exit 0 with no commits → `Unchanged` or `UsageLimited` (detected via JSON output); non-zero exit → `ContinuableFailure` (commits exist) or `Failure`, with bootstrap-failure and usage-limit classification layered on top.

**External-kill classification.**
After the MR-first check, a run whose output has **no result line** is checked for a container killed or removed outside Foundry before falling back to the bootstrap heuristic:

- `WorkerStatus.OomKilled` (Docker `State.OOMKilled`) → `ContainerError("Worker ran out of memory")` — checked first, since an OOM kill also exits 137.
- Exit 137 or 143 (SIGKILL / SIGTERM), not OOM → `ContainerError("Worker container was terminated outside Foundry")`.
- Container `NotFound` → `ContainerError("Worker container was removed outside Foundry")`.
- Any other no-result-line exit whose output holds at least one stream-json event (a JSON object with a string `type`) → `NonZeroExit(exitCode)`: `claude` is the last process the entrypoint starts, so an event proves bootstrap passed and the agent died without a result.
- Any other no-result-line exit with no stream-json event → `WorkerBootstrapFailed` (the sentinel's stage, or the `stage=unknown` heuristic).

Commits still decide `ContinuableFailure` vs `Failure`. Foundry's own stops (timeout, cancellation) never reach this path — they build their outcome via `StoppedRunOutcomeBuilder`.

**Result selection from stream-json.**
The worker runs `claude` with `--output-format stream-json --verbose` ([ADR 0083](docs/adr/0083-live-agent-activity-from-stream-json-over-the-container-log.md)), so the captured output is a stream of one JSON event per line rather than a single result object.
Outcome classification and the Run Result Summary read the **last line whose `type` is `"result"`** — not merely the last JSON line: every other event (`assistant`, `user`, `system`, `rate_limit_event`, unknown types) and partial lines from a container killed mid-event are skipped.
The `result` event is a superset of the former `--output-format json` object (it adds `model_usage_since_last_result`) and maps identically.
One log can hold several `result` events — a transient-retry resume (`claude -c`), or a background subagent's notification re-waking the session — and the last one wins.
Because the stream is far larger than the single result line it replaced, the captured tail (500 lines, capped at 64 KB) is truncated from the **head**, keeping the tail so the final `result` is never cut off.
Output with no `result` event is classified by the no-result-line rules above: any other stream-json event means the agent started (see Container Output Parser); otherwise the bootstrap lines and `FOUNDRY_BOOTSTRAP_FAILED` / `FOUNDRY_DOCKER_UNAVAILABLE` sentinels decide.

**Side-effect applier.**
A single `ApplyOutcomeAsync` performs all side effects in one place: state transition, integration-event dispatch (carrying `WorkerRunMergeState`), usage-limit persistence + dispatch-pause, and container stop-and-remove.
Every terminal outcome (`Completed`, `Review`, `ContinuableFailure`, `Failure`, `Unchanged`) stops and removes the container — this is an invariant enforced at the applier, not a per-path decision.
`Indeterminate` → log and return; container left running for the next tick.

**Timeout watchdog.**
The watchdog ceiling (`StartedAt + timeout_minutes`) is evaluated regardless of whether the container is still running.
Any unresolved run that exceeds the ceiling is processed through the same resolver → applier pair (exit code null; MR-state still consulted first), so no active run can hold a worker slot indefinitely.
The ceiling derives from `StartedAt`, which only `ActiveRun` carries — the watchdog therefore covers active runs only, and a run that never reaches `ActiveRun` is outside it.
Runs stranded in `StartingRun` (never reaching `ActiveRun`) are handled by the periodic sweep instead — see Orphan Reconciliation.

**Docker daemon reachability.**
`GetStatusAsync` returns a discriminated `WorkerStatusProbe` distinguishing three cases:

- `Available` — container exists (running or exited); carries a `WorkerStatus` with `IsRunning`, `ExitCode`, `FinishedAt`, and `OomKilled`.
- `NotFound` — container is definitively gone (Docker 404).
- `Unreachable` — daemon connectivity failure: only `HttpRequestException`, `TimeoutException`, or a self-timeout `OperationCanceledException` (caller's token was NOT already cancelled).
  Any other `DockerApiException` propagates as an unexpected error.

On `Unreachable` the active run is left in `ActiveRun` state (never failed) and recovers on a later tick.
Startup reconciliation is deferred when the daemon is unreachable — the `_reconciled` latch is set only after every per-run `GetStatusAsync` probe returns without an `Unreachable` result; a boot with zero `ActiveRun` rows latches `_reconciled` immediately without contacting Docker.
Orphan-container reaping is not part of the startup reconcile: it has moved to the always-on periodic sweep (`StaleStartingRunService`), which defers its own tick when `ListByLabelAsync` returns `Unreachable` — see Orphan Reconciliation.

A `/ready` readiness health check (tagged `ready`, mapped unconditionally at `/ready`) surfaces Docker daemon connectivity:
healthy when the daemon responds to a ping within 3 seconds, unhealthy with a message identifying Docker daemon connectivity when the ping fails or times out.

## Orphan Reconciliation

The periodic sweep that detects and cleans up containers and runs that have fallen out of the normal lifecycle — implemented by `StaleStartingRunService` (a `PeriodicBackgroundService` in the Workers module, ticking every minute).

**Orphaned container reaping.**
Each tick, the sweep calls `IWorkerOrchestrator.ListByLabelAsync` to enumerate every container carrying the `foundry.managed` label (worker-managed containers), then cross-references the returned run IDs against slot occupancy (see Worker Slot Occupancy).
Any labelled container whose run ID is outside the `starting ∪ active` set is an orphan — its run ended (or was lost) but the container was not cleaned up.
The sweep stops and removes each orphan container immediately.

**Stale `StartingRun` failure.**
After reaping orphaned containers, the sweep loads all `StartingRun` rows and applies the staleness threshold (`StaleStartingRunThreshold` = 10 minutes).
A `StartingRun` older than the threshold is stale: the container either never started or the host died before activation.
For each stale run, the sweep:

1. Reaps the run's container first, if a labelled container with that run ID was found by `ListByLabelAsync`.
2. Calls `StartingRun.Fail(ContainerError("Container did not start within the allowed time."))`.
3. The `WorkerRunFailed` domain event raised by `Fail` flows through `WorkerRunFailedHandler` → `InProgressIssue.MarkFailed()` → `FailedIssue` → the issue becomes retryable from the dashboard (see ADR 0050).

A `container_error` failure does not auto-retry (`container_error` has no automatic retry schedule, so `TransientRetryService` never loads it) — the issue parks under "Needs you" for operator review, consistent with [ADR 0014](docs/adr/0014-remove-immediate-requeue-always-pause.md).
A `WorkerRunFailed` event whose run ID does not match the issue's current `WorkerRunId` is silently ignored by `WorkerRunFailedHandler` (stale-run-ID guard), so a concurrent transition cannot produce a double failure.
The same guard applies to `WorkerRunCompletedHandler`: a `WorkerRunCompleted` event whose run ID does not match is rejected as stale, so a superseded run cannot drive a completed transition on an issue that has already moved on.

**Daemon-unreachable defer.**
When `ListByLabelAsync` fails with a daemon connectivity error (detected by `DockerDaemonConnectivity.IsUnreachable`), the sweep logs a warning and returns without reaping or failing anything — both phases are skipped for that tick.
Per-run failure attempts that encounter Docker errors are also best-effort: a `TryStopAndRemoveAsync` failure is logged but does not abort the failure transition for the run.

**Orphaned data-root volume reaping.**
After the container and `StartingRun` phases, the sweep calls `IWorkerOrchestrator.ReapOrphanedDataRootVolumesAsync(slotRunIds)`, passing the current slot-occupancy run IDs.
The orchestrator lists every Docker volume labelled `foundry.worker-run-id` and removes those whose run ID falls outside slot occupancy — host-crash orphans whose worker container was cleaned up but whose data-root volume was never deleted.
Each removal is best-effort: a failure on one volume is logged but does not prevent the remaining volumes from being processed.
This phase is covered by the same daemon-unreachable defer: when `ListByLabelAsync` earlier in the tick returned `Unreachable`, the whole tick is skipped before reaching this phase.

## Container Output

The captured tail of a finished worker container's stdout/stderr, stored on both `CompletedRun` and `FailedRun` as a nullable string.
Captured by Foundry (not the worker) from the Docker API after the container stops but before removal; when the tail exceeds the 64 KB cap the head is dropped so the final `result` event survives (see Worker Outcome Detection).
Best-effort — null when the container is already gone, never started, or the Docker API call fails.
Docker timestamps are enabled (`--timestamps` flag) so each output line carries an RFC 3339 prefix, providing a unified timeline.

Stored output of a completed or failed run is served by `GET /api/workers/runs/{workerRunId}/log` (200 with the run's Run Transcript when one is stored, else the stored output rendered as an Agent Transcript; 204 when neither exists; 404 when the run is unknown) and rendered in the dashboard by the shared `fd-log-view` component (static source mode) in the issue detail panel.

Running workers stream live output via the `WorkerHub.StreamLog(workerRunId)` method on `/hubs/workers` — the hub method returns an `IAsyncEnumerable<string>` of redacted log lines, rendered as an Agent Transcript, with backlog replay followed by live follow.
The dashboard's `fd-log-view` component subscribes to this stream (stream source mode) and renders the transcript lines in arrival order.

## Run Transcript

The Agent Transcript of a worker run's full container log, rendered by `AgentTranscriptFormatter.FormatLog` when the run completes (exited, timed out or cancelled), before the container is removed, so a run can be diagnosed after its container is gone.
Where the Container Output keeps only the tail for outcome classification, the Run Transcript covers the whole run, including both sessions of a run resumed after a transient API error.
Stored one row per run in `worker_run_transcripts` (`worker_run_id` primary key and foreign key to `worker_runs`, `transcript`, `captured_at`), written in the same save as the run's terminal transition and never loaded with the `WorkerRun` entity.
Capped at 256 KB of UTF-8 (`WorkerRunTranscript.MaxTranscriptBytes`): a longer transcript keeps its head and tail, cut on line boundaries, with a marker line in between stating how many lines and bytes were omitted; a transcript exactly at the cap is stored whole.
Best-effort — when the full-log fetch or the rendering fails, or the container is already gone, no row is written and the run's outcome, terminal transition and Container Output are unaffected.
Deleted 30 days after `captured_at` by `RunTranscriptRetentionService`, a throttled sweep deleting in bounded batches (a full batch re-arms the sweep, an underfull one restores the throttled cadence); the run row, its Container Output and its summary stay.
Served by `GET /api/workers/runs/{workerRunId}/log` as stored, in preference to rendering the Container Output tail.

## Agent Transcript

The readable, summary-only rendering of a worker's stream-json container log, shown in both log views (the live `WorkerHub.StreamLog` stream and the stored run log) instead of raw JSON, and persisted per run as the Run Transcript.
Produced server-side by `AgentTranscriptFormatter` — one formatter shared by both views, so the event vocabulary is interpreted only in C#.
It emits one line per meaningful event: a tool call with an input summary (Bash `command` collapsed and truncated, Edit/Write/Read `file_path`, Agent `subagent_type` + `description`, Skill name), the tool's success or failure with its duration, assistant text, subagent start and finish (`task_started` / `task_notification`), session start, and the final `result`.
Events from inside a subagent are indented and labelled with the subagent type.
`tool_result` bodies are never rendered — only a truncated error message when a tool fails — and thinking, `system/thinking_tokens`, hooks, task progress, rate-limit and unknown events are omitted; non-JSON lines (bootstrap output, stderr, sentinels) pass through unchanged, and the Docker timestamp prefix is kept.
Secret redaction (`SecretRedactor`) applies to every raw line before it reaches the formatter, and again to each rendered line.
The full, raw event stream remains available only in `docker logs` while the container exists.

## First-Run Wizard

A guided setup flow (`/setup`) that runs when no accounts are configured.
Three steps: select auth mode (API key or OAuth), add first account (provider, base URL, PAT — the account name is derived from the token identity), select repositories to monitor (fetched from provider API).
Auto-redirects from `/issues` when no accounts exist. Redirects to `/issues` on completion.
The wizard reuses the same form components as the settings page.

## FailureReason

A value object on FailedRun that classifies how the run failed.
Variants: `NonZeroExit(exitCode)` (container exited with non-zero code), `TimedOut` (exceeded configured timeout), `ContainerError(message)` (Docker-level failure — image not found, daemon unavailable, etc.), `UsageLimited(resetsAt)` (worker hit a *time-based* Anthropic API usage limit — session, weekly, or Opus quota — carrying a parseable reset time; token `usage_limited`), `CreditsExhausted` (worker hit a *money-based* 429 — credit pool empty, org monthly spend limit reached, or the CLI ≥ 2.1.119 regression — carrying no reset time; token `credits_exhausted`; summary `"Credits exhausted"`, which deliberately shares no prefix with the usage-limit summary so the auto-resume `LIKE 'Usage limit reached%'` sweep cannot catch it; blocks the account's spend indefinitely until an operator resumes), `WorkerBootstrapFailed(detail)` (pre-task failure — the worker container died during entrypoint bootstrap, before `claude` ran; carries a short, secret-redacted diagnostic `Detail` with the failed stage and error tail), `AuthInvalid` (the worker's Claude credentials were rejected — `api_error_status == 401` or `error.type == "authentication_error"`; triggers the auth-invalid pause), `TransientApiError` (a transient Anthropic API fault the run neither caused nor can fix — `api_error_status` in the 5xx range, or `is_error: true` with `api_error_status: null` and a `result` matching a known transient phrase such as a mid-response connection drop or `529 Overloaded`; token `transient_api_error`; drives the bounded auto-retry described under Transient Retry), `ProviderError(message)` (a provider API call needed to start the run failed, e.g. branch pre-creation rejected with 403; raised before the worker task begins), `ProviderUnreachable(detail)` (the [Publisher](#publisher)'s Prepare could not reach the git provider on any of its attempts — `FOUNDRY_BOOTSTRAP_FAILED stage=unreachable`; carries git's last error, secret-redacted; token `provider_unreachable`; summary `"Could not reach the git provider: <detail>"`; retried automatically on its own, longer schedule described under Transient Retry), `Cancelled` (a user cancelled the run and Foundry stopped its container — see Run Cancellation; token `cancelled`; summary `"Worker run cancelled"`; never auto-retried).
`ContainerError` also carries the external-kill classifications described under Worker Outcome Detection (`"Worker ran out of memory"`, `"Worker container was terminated outside Foundry"`, `"Worker container was removed outside Foundry"`).
Each variant's `CategoryToken` value flows from the matching `FailureCategory` const (see FailureCategory below), making `FailureCategory` the single source of truth for the token vocabulary.

## FailureCategory

A value object in `Foundry.Modules.Workers.Contracts` owning the eleven-token vocabulary that names *why* a run failed, and the single owning type for that vocabulary (see [ADR 0068](docs/adr/0068-failurecategory-value-object-owns-token-vocabulary.md)).
The eleven tokens are the ten `FailureReason` category tokens (including `cancelled`) plus `pr_closed`.
`FailureReason`'s ten `CategoryToken` values derive from the matching `FailureCategory` const fields, so both producers obtain their value from the owning type: `WorkerRunFailedHandler` converts `FailureReason.CategoryToken` at the domain boundary, and `ProviderPullRequestClosedHandler` uses `FailureCategory.PrClosed` directly.
The entity property on `FailedIssue`, `ContinuableFailedIssue`, and `RevisionFailedIssue` is typed `FailureCategory` and persisted via an EF `ValueConverter` to the unchanged `TEXT` column (`failure_category`) — no migration needed, and the `TransientRetryService` predicate stays SQL-translatable by testing membership in `FailureCategory.AutoRetryable` (`IN (…)`).
The converter's read direction is lenient: unknown stored tokens coalesce defensively to `FailureCategory.NonZeroExit` without throwing, so a stale or legacy row never disables auto-retry. The domain-boundary conversion in `WorkerRunFailedHandler` remains the point that rejects and logs unknown tokens from incoming events.
The `WorkerRunFailed` contract `Category` field remains `string?`; rejection of unknown or null values happens at the domain boundary in `WorkerRunFailedHandler`, which falls back to `FailureCategory.NonZeroExit` (logging a warning) so the issue always transitions to failed.
Drives category-conditional recovery: each category carries an optional `AutomaticRetry` schedule (`transient_api_error`: 2 retries, 1 minute each; `provider_unreachable`: 5 retries, 2–32 minutes doubling), and those with one are `FailureCategory.AutoRetryable` — the bounded auto-retry described under Transient Retry.

## Usage Limit

A *time-based* state where the Anthropic API quota (session, weekly, or Opus limit) is exhausted and self-heals at a known reset time.
Detected by parsing the worker container's JSON output (`--output-format json`): the primary signal is `ResultMessage.api_error_status == 429`; the `terminal_reason` allowlist (`"blocking_limit"`, `"rapid_refill_breaker"`) is retained as a secondary signal for older output shapes. Note that a 429 can arrive with `subtype: "success"` and `terminal_reason: "completed"`, so neither field is reliable on its own.
The reset time is extracted from the human-readable result text. Two wall-clock wording shapes are handled: `resets <time> (UTC)` (e.g. `"You've hit your limit · resets 12:10am (UTC)"`) and `reset at <time> (<IANA zone>)` (e.g. `"Your limit will reset at 3pm (America/New_York)."`). In both forms the time is 12-hour; a bare hour without minutes (e.g. `3pm`) defaults minutes to `00`. UTC times resolve to the next future UTC occurrence directly; IANA zone names are converted to UTC via the system timezone database, then the same roll-forward applies. ISO-8601 timestamps in the result text take precedence over wall-clock parsing.
Whether a reset time parses is the discriminator between the two 429 classes (see [ADR 0046](docs/adr/0046-parseable-reset-time-as-usage-limit-discriminator.md)): a 429 with a parseable reset time is a `UsageLimited` time-based limit; a 429 with no parseable reset time is a money-based block classified as `CreditsExhausted` (see FailureReason and Dispatch Pause). There is no fabricated-cooldown fallback — the former `DefaultCooldownMinutes` setting is removed.
A detected usage limit always triggers a global dispatch pause via `GlobalSettings.UsageLimitResetsAt` — there is no immediate-requeue path. `GlobalSettings.SetUsageLimitResetsAt` remains extend-only and clamps to 7 days, so a reset time already in the past only ever extends an existing pause.
The triggering issue transitions to `FailedIssue` / `ContinuableFailedIssue` with `FailureReason.UsageLimited(resetsAt)`.
On detection, `WorkerDispatchService` raises the `DispatchPaused` integration event, which is broadcast as a `dispatch` system notification (`isActive: true`) so the dashboard usage-limit banner updates live without a page refresh.

## Dispatch Pause

A global operational state where the dispatch loop skips issuing new work.
Two independent global triggers on `GlobalSettings`: `UsageLimitResetsAt` (automatic, from usage limit detection) and `IsDispatchPaused` (manual, from operator "Pause All" action).
Dispatch is paused when either is active.
Auto-resume clears `UsageLimitResetsAt` and retries all `FailureReason.UsageLimited` issues when `AutoResumeOnUsageReset` is enabled.
Manual "Resume All" clears both flags and every [Host Pause](#host-pause), and retries usage-limited issues.

A third, independent pause is the **credit block**: when a run fails with `CreditsExhausted`, the `ClaudeAccount` aggregate's `SpendState` transitions to `Blocked(NextProbeAt)`, and `CredentialGate.CanDispatchAsync` then refuses dispatch regardless of the global flags. This pause carries no reset time — a money-based block cannot say when it clears — but it clears without an operator once the account can spend again (Team credits auto-reload, an admin raises the spend cap), so the block schedules a **Credit Probe** to poll for that moment (see Credit Probe). `SpendState` and `CredentialValidity` are independent: an account can be simultaneously credit-blocked and auth-invalid, the gate refuses on either, and clearing one leaves the other in force. Both a successful probe and a manual "Resume All" call `RestoreSpend()` and — only when the state actually changed — publish `CreditsRestored`, on which the Issues module re-queues every issue whose `FailureReason` equals `"Credits exhausted"` (an exact match, never a prefix sweep); the transition is idempotent so `CreditsRestored` publishes exactly once even when a probe and a manual resume converge.
Already-running workers are unaffected — only queued issues are held.
Both pause and resume broadcast a `dispatch` system notification (`isActive: true` on pause, `isActive: false` on resume); the client treats this as a pure reload trigger and re-syncs banner state from `/api/settings` (the authoritative source) rather than reading any timestamp off the SignalR payload.

## Host Pause

Dispatch and publishing held for one provider host (for example `gitlab.com`) because a run's [Publisher](#publisher) found it unreachable. Other hosts dispatch and publish as normal, so a gitlab.com outage never stops GitHub work. Queued issues for the paused host wait in the queue instead of each dispatching, failing and spending its own retry budget, and finished runs wait to publish instead of spending their publish attempts.
- **State:** `GlobalSettings.HostPauses`, one immutable `HostPause(Host, PausedAt, ProbeAttempts, NextProbeAt)` per host, beside the other [Dispatch Pause](#dispatch-pause) reasons. A new pause has `ProbeAttempts = 0` and `NextProbeAt = PausedAt + 1 min`; each probe that does not clear it at `now` adds one attempt and sets `NextProbeAt = now + min(2^(ProbeAttempts+1) min, 10 min)`, so probes run 1, 2, 4 and 8 minutes apart, then every 10 minutes, without limit (`HostPause.AfterUnreachableProbe`).
- **Trip:** a Prepare that fails `ProviderUnreachable` calls `GlobalSettings.PauseHost` with the repository's host (`MonitoredRepository.Host`, carried as `RepositoryDispatchInfo.Host`) in the same save as the run's failure (`IssueClaimedHandler`), and so does a publish attempt that fails `kind=unreachable` (`PublishResult.Failed` with `PublishFailureKind.Unreachable`, saved with the run's `PublishFailedRun`, `WorkerRunPublisher`). A host already paused is left as it is, so concurrent failures never reset its backoff. No other Prepare or publish failure trips it.
- **Gate:** `DispatchCandidateSelector` drops every repository whose host (`EligibleRepository.Host`) is in `DispatchPauseState.PausedHosts` before ordering candidates, which holds every queued kind (fresh, triage, revision, continuation). When every repository with queued work is on a paused host the selector returns `NoCandidates` and the slot is released through `ClaimSkipped`.
- **Publish wait:** the publishing loop (`RunPublishingService`) holds every run whose repository's host (`RepositoryDispatchInfo.Host`, matched case-insensitively against `DispatchPauseState.PausedHosts`) is paused: a `PublishFailedRun` whose retry is due is not retried and a `PublishingRun` is not published, so the run is left untouched and an outage spends none of its publish attempts. It retries or publishes on the first tick after the pause clears (by a probe or Resume All). The pause never extends the Hand-off time to live: a held `PublishFailedRun` or `PublishBlockedRun` past `DiscardsAt` is discarded as usual.
- **Probe:** `HostProbeService` (every 15 s) probes each paused host whose `NextProbeAt` has passed, never two probes of one host at once, and keeps probing during a global manual or usage-limit pause. `HostProber` tries up to 3 monitored repositories on the host, one Publisher Probe container after another, each with its token resolved as Prepare resolves it: most recent successful Prepare first (`WorkerRun.PreparedRepositoryId`, recorded with the run's base), repositories never prepared last. The first `reachable` calls `ClearHostPause`; when every probe is `unreachable` or inconclusive (container error, no `FOUNDRY_PROBE` line, a 2-minute timeout, or no repository with a usable token, each logged as a warning) it calls `RecordProbeUnreachable`. Probing through the Publisher, rather than from the WebApi process, uses the containers' own network path and the same `classify_error` as the trip, so the trip and the release can never disagree about what unreachable means.
- **Manual fallback:** "Resume All" clears every host pause (`ResumeAllDispatch`); the next dispatch to a still-unreachable host trips it again, with probing restarting at 1 minute.
- **Broadcast:** adding, advancing or clearing a host pause broadcasts a `dispatch` system notification once the change is saved, so the dashboard reloads its settings (Resume All's own `DispatchResumed` broadcast covers its clearing). Clearing a host never publishes `DispatchResumed`, whose consumer re-queues usage-limited failures.

## Credit Probe

The mechanism that auto-clears a credit block. Since the clearing moment is unknowable, Foundry polls for it with a cheap transient container rather than re-dispatching real work — a full resume would re-queue every stranded issue each cycle (N worker containers, N×2+ provider API calls, N `worker_runs` rows, N failed transitions, N broadcasts), while a probe reads the same single bit ("can this account spend right now?") with one short container and no issue state touched.

`SpendState.Blocked` carries a durable `NextProbeAt` — kept on the aggregate (not an in-memory timer) so the schedule survives a host restart. `CreditProbeService` (a `PeriodicBackgroundService` in the Credentials module, mirroring `TransientRetryService`) ticks ~30 s, and when `NextProbeAt` has passed it delegates to `CreditProbeCoordinator.TryRunProbeAsync`. The coordinator holds an in-process `SemaphoreSlim(1,1)` for single-flight — a second concurrent caller (a scheduled tick racing the operator's "Check now") is a no-op that reports the in-flight probe. It defers while `ILoginSessionState.IsLoginActive` (the same window `CredentialGate` refuses on) and logs-and-skips when no `ClaudeAccount` row exists.

The probe container mirrors the login/credential-helper containers: transient (`foundry.transient=true` so existing reaping applies and worker reaping never touches it), `foundry.role=credit-probe`, `AutoRemove`, built from the login image, running a trivial `claude -p` prompt under its own `timeout`, mounting the credential volume read-write at `/home/node/.claude` (OAuth; read-write like workers, though workers mount it only as their credential store — the CLI may refresh and rotate the token during the probe) or injecting `ANTHROPIC_API_KEY` (API key). Its captured logs are classified by `IProbeOutcomeClassifier` (a public surface in `Workers.Contracts` wrapping the internal `IContainerOutputParser`, so the money-vs-time-vs-infra discrimination has a single source of truth) into a `ProbeOutcome`:

- `Available` → `RestoreSpend()`; if changed, publish `CreditsRestored` (outbox) → Issues re-queues.
- `CreditsStillBlocked` (money-based 429, no reset time) → `RearmProbe(now + ProbeIntervalMinutes)`; no issue state changes.
- `UsageLimited(resetsAt)` (time-based 429, reset time parses) → set `GlobalSettings.UsageLimitResetsAt` and clear the credit block (the money path is proven fine, so the usage-limit pause takes over) rather than blindly re-arming the probe.
- `InfrastructureFailure` (container start error, timeout, or unclassifiable output) → `RearmProbe(now + ProbeIntervalMinutes)` with no `SpendState`/issue change; the banner deliberately does not report a credit problem for an infrastructure fault.

The operator can force an immediate probe regardless of `NextProbeAt` via `POST /api/credentials/probe` ("Check now"), which returns an in-flight indicator (`202`) when a probe is already running. `NextProbeAt` is exposed on the `/api/credentials` payload so the dashboard `credits` banner renders a live countdown. Container hardening for this new container type is deferred to #60.

## Usage Snapshot

A point-in-time view of the Claude subscription's usage limits, mirroring what Claude Code's `/usage` command reports — the proactive counterpart to the Credit Probe's reactive money-block signal.
Modeled as an immutable value object (`UsageSnapshot`) holding the list of rate limits (`UsageRateLimit`: `Kind` in `session` / `weekly_all` / `weekly_scoped`, `Percent`, optional `ResetsAt`, optional `ModelDisplayName`, `Severity`, `IsActive`), optional extra-usage credits (`UsageExtraCredits`: `IsEnabled`, optional `MonthlyLimit`, `UsedCredits`, optional `Utilization`, `Currency`, in minor units — `50000` USD is 500.00 USD), the `CapturedAt` time, the `AccountEmail` it was captured for, the `Source` it was read from (`usageReport` — the `/usage` report — or `probeTurn` — the fallback; see Usage Poll), and an optional `LastRefreshFailedAt` stamp.
On a `usageReport` snapshot `Severity` is Anthropic's own `normal` / `warning` / `critical` classification; on a `probeTurn` snapshot it is derived server-side from the window utilization (>= 1.0 critical, >= 0.8 warning). Either way it is carried as a string the whole way to the wire so the client applies no thresholds of its own and tolerates an unrecognised value by falling back to a neutral colour.
The credits carry a server-computed `Percent` (the reported `Utilization` when present, otherwise `UsedCredits / MonthlyLimit * 100`) and `Severity` (`critical` at >= 100, `warning` at >= 80, else `normal`), both computed on the `UsageExtraCredits` value object and both null when `MonthlyLimit` is null or 0 (amount-only credits).
The snapshot is provider-owned, stale-by-definition data, so it is held only in an in-memory singleton (`UsageSnapshotState`, mirroring Docker Availability State) — no table, no migration; a startup poll re-establishes it after a restart.
A successful poll replaces the snapshot and clears any failure stamp; a failed poll (Docker unavailable, container timeout, non-zero exit, no stream-json output, an unparseable `usage_report`, or a failed probe-turn fallback) retains the last good snapshot and stamps `LastRefreshFailedAt` so the dashboard dims it and shows "Couldn't refresh"; a poll that legitimately returns no usage (API-key mode — no `usage_report` emitted) clears the snapshot rather than marking it failed. A `usage_report` whose `rate_limits` is null or absent is neither: it triggers the probe-turn fallback (see Usage Poll).
Delivered by `GET /api/credentials/usage` (204 when absent) for the initial seed and pushed on the system hub via a dedicated `UsageSnapshotUpdated` method — not a System Notification category — on every poll, failure, and clear, so the "As of" capture time never goes falsely stale on an idle system.

## Usage Poll

The act of capturing a Usage Snapshot: a transient helper container (`foundry.transient=true`, `foundry.role=usage`, `AutoRemove`, built from the login image) runs `claude -p "/usage" --output-format stream-json --verbose`, and `UsageReportParser` reads the `usage_report` sibling of the emitted assistant message (`rate_limits.limits[]` and `rate_limits.extra_usage`).
When that report's `rate_limits` (or its `limits`) is null or absent — a print-mode regression seen across CLI versions — the parse yields `RateLimitsMissing` and the same poll runs a second container afterwards, sequentially: the probe turn `claude -p "Reply with: ok" --max-turns 1 --output-format stream-json --verbose`, whose `rate_limit_event` `unifiedWindows` `ProbeTurnUsageParser` reads into `session` / `weekly_all` rows. The resulting snapshot keeps any credits `/usage` still reported, has `Source = probeTurn`, and the poll logs a warning naming the CLI version from the stream-json `init` line. If `/usage` fails outright, no fallback runs (a failed poll); if the fallback probe fails, the poll fails too. See [ADR 0099](docs/adr/0099-usage-report-first-with-a-probe-turn-fallback.md).
Like the Credit Probe (and workers, which mount it as their credential store at `/home/node/.claude-credentials`), the usage container mounts the credential volume **read-write** at `/home/node/.claude`: the CLI may refresh an expired access token during the call, and a read-only mount cannot persist the rotated refresh token, leaving a dead token on the volume.
Gated on `ClaudeAccount.CanReportUsage` — true only for OAuth mode with a committed account identity and `Validity` not Invalid, and deferred while `ILoginSessionState.IsLoginActive`; spend state does not gate it, so usage stays visible during a credit block.
`UsagePollService` (a `PeriodicBackgroundService`) ticks every 5 minutes and once at startup, and `CredentialsValidated` / `CredentialsInvalidated` / an auth-mode change each clear the snapshot and trigger an immediate poll when the account can report usage.
All of these run through `UsagePollCoordinator`, which holds a `SemaphoreSlim(1,1)` for single-flight but, unlike the Credit Probe's fail-fast coordinator, has a second concurrent caller **join** the in-flight poll rather than being dropped (see [ADR 0081](docs/adr/0081-usage-polling-single-flight-joins.md)), so an event-triggered poll arriving during a scheduled tick still refreshes without starting a second container.

## System Notification

A lightweight SignalR broadcast (`Category`, `IsActive`, `Message`) delivered to all dashboard clients via the system hub, used for global operational banners.
Categories in use: `dispatch`, `docker`, `image-build`, `credits`.
Auth-invalid and auth-restored conditions broadcast on `dispatch` (they gate dispatch via `ClaudeAccount.Validity`), not on a separate category.
Client semantics: an `isActive: true` notification is stored, replacing any prior entry for its category; `isActive: false` removes the category's entry.
Both `dispatch` and `image-build` are pure reload triggers — each broadcast carries an empty message and signals the client to re-sync authoritative state from `/api/settings` rather than reading any payload (see Dispatch Pause and Worker Image Build State).
The `credits` category (see Credit Probe) is its own reload trigger — `isActive: true` on credit block or a probe re-arm, `isActive: false` on restore — re-syncing the countdown from `/api/credentials`. It is deliberately independent of `dispatch`: an auth-invalid condition and a credit block are independent and can hold at once, so each holds its own category and both banners render simultaneously rather than one replacing the other.
Ephemeral — broadcast directly, never through the transactional outbox, since there is no durable consumer.

## Container Output Parser

An infrastructure service (`IContainerOutputParser`) that classifies a worker container's JSON output.
Takes the captured `--output-format stream-json` output, selects the last `type: "result"` event (see Worker Outcome Detection), and returns a discriminated result: `NormalExit`, `UsageLimited(DateTimeOffset ResetsAt)`, `CreditsExhausted`, `AuthInvalid`, `TransientApiError`, `ParseFailure(string RawOutput)`, `NoResultLine`, `AgentStartedNoResultLine`, or `WorkerBootstrapFailed(string Detail)`.
Inspects `ResultMessage.api_error_status` (429) as the primary limit signal, with the `terminal_reason` allowlist as a secondary signal. When a limit signal is present, reset-time parse **success** is the discriminator (see ADR 0046): if a reset time is extracted from the result text via best-effort regex (wall-clock or ISO-8601) the result is `UsageLimited(resetsAt)`; if none parses the result is `CreditsExhausted`. `Parse` takes no cooldown fallback parameter — the former fabricated `DefaultCooldownMinutes` reset time is removed.
Auth failures are detected from `api_error_status == 401`, with `error.type == "authentication_error"` as a secondary guard.
Transient API faults are detected from `api_error_status` in the 5xx range (the durable signal), or — when `api_error_status` is null and `is_error: true` — from a narrow frozen allow-list of transient `result` phrases (`"API Error: Connection closed mid-response"`, `"API Error: 529 Overloaded"`) matched case-sensitively as a substring. The predicate is status-5xx-or-allow-list, never a bare `is_error: true`, so genuine task failures are never laundered into a retryable category. When `is_error: true` matches no known category (usage-limit, auth, transient), the parser logs a warning naming the unclassified `result` text — a tripwire for a reworded transient phrase that the brittle allow-list would otherwise miss silently.
Bootstrap failures are detected via a sentinel line (`FOUNDRY_BOOTSTRAP_FAILED stage=… detail=…`) emitted by `entrypoint.sh` when the container dies before `claude` runs (clone, auth, branch, managed-settings, or role stage; the [Publisher](#publisher)'s Prepare also prints it, with stage `clone` or `unreachable`); the parser scans for the sentinel only when no Claude JSON result line is present, so a genuine result always wins. When there is no result line but at least one line parses as a stream-json event (a JSON object with a string `type`), the agent demonstrably started — bootstrap output goes to stderr and `claude` is the last thing the entrypoint runs — so the parser returns `AgentStartedNoResultLine` and never `WorkerBootstrapFailed`, whatever sentinel text the output (e.g. a tool result) contains; the resolver maps it to `NonZeroExit(exitCode)` after the external-kill checks. Otherwise the sentinel counts only at the start of a line (after the optional Docker timestamp), so a mid-line quote is ignored, and when the 64 KB tail cut lands mid-line the partial first line is dropped before matching. A non-zero exit with no result line, no stream-json event and no sentinel falls back to `WorkerBootstrapFailed` heuristically.
Domain types remain JSON-unaware — all parsing is in infrastructure.
`IContainerOutputParser` and `ContainerOutputParseResult` are `internal` to the Workers module; the Credentials credit probe (which runs outside Workers) reaches the same classification through `IProbeOutcomeClassifier`/`ProbeOutcome` in `Workers.Contracts`, a public wrapper that maps each parse result to the coarser probe outcome (`Available`, `CreditsStillBlocked`, `UsageLimited`, `InfrastructureFailure`) so there is one source of truth for the money-vs-time-vs-infrastructure discrimination.
