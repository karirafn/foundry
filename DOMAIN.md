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
A worker restores private [Package Sources](#package-source) through the [Package Source Proxy](#package-source-proxy) ([ADR 0112](docs/adr/0112-workers-restore-private-nuget-package-sources-through-a-foundry-proxy.md)): when its repository declares one, Foundry passes a generated `nuget.config` in `FOUNDRY_NUGET_CONFIG` (base64) — the repository's root config with each declared source re-pointed at the proxy and its credentials replaced by the run's [Run Source Token](#run-source-token) — and the entrypoint writes it to `~/.foundry/nuget.config` and exports `RestoreConfigFile` with that path before the C# LSP pre-restore. It also passes `FOUNDRY_PACKAGE_PROXY_URL`, the proxy's base URL as the worker sees it (`PackageSourceProxyOptions.WorkerBaseUrl`, carrying no secret); the entrypoint requests it once, unauthenticated, with a 10-second timeout before the C# LSP pre-restore and before `claude` starts, even when the config did not decode. Any HTTP response, whatever its status, is reachable; no response (DNS failure, refused or unreachable connection, timeout) fails bootstrap with stage `package-sources` and a detail naming the proxy's host and port, and the run fails `PackageSourceUnreachable` (category `package_source_unreachable`), retried automatically as described under [Transient Retry](#transient-retry). Because Foundry owns that restore path, a full-capability run (`WORKER_CAPABILITY=full`) given a non-empty `FOUNDRY_NUGET_CONFIG`, decodable or not, whose C# LSP pre-restore exits non-zero other than by exceeding its time budget (exit 124 or 137, which still only disables the LSP) fails bootstrap with stage `package-restore` before `claude` starts; the detail is the failing target's workspace-relative path, `: `, then the first restore output line containing `error` (trimmed), or `dotnet restore exited <code>` when there is none. The run fails `PackageRestoreFailed` (category `package_restore_failed`), retried automatically on the same schedule and without pausing dispatch. Restricted runs (triage, review) and repositories without Package Sources keep "C# LSP disabled, continue". MSBuild reads that variable as a property, so `dotnet restore`, `build`, `test` and `add package` read it instead of the tracked `nuget.config`, which stays untouched; `dotnet tool restore` does not honour it. A repository using nuget.org only gets neither variable. Every worker container carries the host entry `host.docker.internal:host-gateway` so it reaches the proxy on a Linux Docker Engine as on Docker Desktop.
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
The login image sets no entrypoint; the startup command Foundry supplies bootstraps a named FIFO at `/tmp/ci`, writes the FIFO's writer sleep-holder PID to `/tmp/ci.pid`, then starts `claude auth login --claudeai` — wrapped in `timeout -k 10 <session-timeout>` — with the FIFO wired to stdin so the process blocks waiting for the authorization code.
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
`ICredentialsOrchestrator.SeedOnboardingAsync` idempotently merges onboarding-gate flags into the volume's `.claude.json` (`hasCompletedOnboarding`, `hasTrustDialogAccepted`, `theme`), setting each key only when absent, so existing credentials and account data are never overwritten.
No login flow calls it: the login container starts `claude auth login --claudeai` against the volume as it stands.

**Session lifecycle.**
At most one login session is active at a time — `LoginSessionService` (singleton, in-memory, Credentials module) enforces this.
Dispatch is transiently suppressed while a session is active via `ILoginSessionState.IsLoginActive` (checked by `WorkerDispatchService`); no pause record is persisted.
Session state is not persisted: a Foundry restart mid-login leaves the in-memory session gone.
In-process, `LoginSessionService.SubmitCodeAsync` tears the container down in a `finally`, so every exit path — success, invalid code, timeout, host shutdown — removes it exactly once (a second remove is a safe no-op).
For crashes where no in-process path runs, the `timeout` wrapper and `AutoRemove` end and remove the login container on their own, and `TransientContainerReaper` (a periodic background service, 60 s) backs them up by sweeping only non-running transient containers created more than 30 minutes earlier (the residue when `AutoRemove` misses), so it never removes a live login or in-flight helper while a session is active.
The reaper — and all transient login and credential-helper containers — key off the `foundry.transient=true` label, deliberately distinct from the `foundry.managed`/`foundry.worker-run-id` labels on long-lived worker containers, so reaping never touches a running worker.
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

The worker image settings stored on Global Settings: the **image tag** (default `foundry-worker:local`; empty or `:latest` rejected) and the **build toggle** (default enabled).
Every image installs the full toolchain (the .NET SDK with `dotnet-ef` and `csharp-ls`, the Angular CLI, headless Chromium, the Docker engine, and `playwright-core` beside the Claude Code CLI to drive that Chromium) unconditionally, so the configuration holds no install flags and the build passes no toolchain build args ([ADR 0109](docs/adr/0109-the-worker-image-installs-every-toolchain-and-reports-what-it-has.md)); stored rows that still carry the removed `Install*` members deserialize with them skipped.
Any change publishes `WorkerImageSettingsChanged`; with build enabled this requests an immediate rebuild, with build disabled it triggers an inspect (see Worker Image Build).

Beside it, but outside the configuration, Global Settings holds the **nested-Docker switch** (`NestedDockerEnabled`, column `nested_docker_enabled`, default off; shown in Settings as "Nested Docker for implement runs"): whether implement runs get the nested rootless Docker daemon and its relaxed allowances ([ADR 0110](docs/adr/0110-nested-docker-privileges-stay-an-operator-switch.md)).
It governs privileges, not image contents: it is saved on its own (`PUT /api/settings/nested-docker`), publishes no `WorkerImageSettingsChanged` and starts no rebuild, and applies to runs dispatched afterwards — running containers keep their spec.
The migration that added it took its value from the stored `InstallDocker` member (off when absent).

### Worker Image Toolchain

What a worker image actually carries, read from the image rather than assumed from its Dockerfile ([ADR 0109](docs/adr/0109-the-worker-image-installs-every-toolchain-and-reports-what-it-has.md)): a list of `{name, version}` entries (`node`, `claude-code`, `dotnet-sdk`, `csharp-ls`, `angular-cli`, `chromium`, `docker`, `playwright-core`, `gh`, `glab`, `git`; an absent tool is omitted, and `dotnet-sdk` appears once per installed SDK — every `active` or `maintenance` channel of Microsoft's release index, minus any without an apt package) printed by `/opt/foundry/toolchain.sh` as one `FOUNDRY_TOOLCHAIN <json>` line.
After every successful build, and every successful inspect when building is disabled, Foundry runs the script in a hardened, short-lived container of that image ID and stores the list in `worker_image_toolchains`, keyed by image ID; earlier images keep their rows.
While the current image (the role catalog's image ID) has no stored list, the probe is retried with backoff (1 minute, doubling, capped at 30 minutes); a new build restarts it.
The toolchain-dependent prompt sections follow the current image's list: the C# code-navigation section when it has `csharp-ls`, the pre-PR section when it has `docker`, the "Rendering UI changes" section (full runs only) when it has both `chromium` and `playwright-core`; an image with no recorded list gets none of them.
Each run also records its own toolchain: the entrypoint runs the script once, after the privilege drop and before the nested `dockerd`, and writes its line to stderr; Foundry reads it from the container's full log and stores it on the run (`worker_runs.toolchain`), shown as the issue pane's Toolchain fact. A run whose script failed, whose bootstrap failed first, or that predates this has none.

### Docker-Enabled Worker Run Spec

When the nested-Docker switch is on, an implement (`Full`) run's worker runs its own nested rootless `dockerd` so Testcontainers-based integration tests work inside the container; Foundry then passes `FOUNDRY_NESTED_DOCKER=1`, which the entrypoint requires before starting the daemon.
This requires the relaxed allowance set from [ADR 0079](docs/adr/0079-relaxed-allowances-for-nested-rootless-docker.md): `CAP_SYS_ADMIN`, `seccomp=unconfined`, `apparmor=unconfined`, unmasked `/proc` system paths (empty `MaskedPaths`/`ReadonlyPaths` — the Engine API form of the CLI's `systempaths=unconfined`), and the `/dev/net/tun` device.
Only `Full` runs get the daemon and these allowances: a `Restricted` run has no tool that could use Docker ([ADR 0098](docs/adr/0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md)), so its spec carries none of them, no data-root volume is created for it, and its entrypoint starts no daemon, whatever the switch says.
A named per-run data-root volume (`foundry-worker-docker-<workerRunId>`, labelled `foundry.managed=true` + `foundry.worker-run-id=<id>`) is mounted at `/home/node/.local/share/docker`; the nested daemon uses native overlayfs on this volume, so `/dev/fuse` is not required.
The isolation boundary weakens: the worker runs as uid 1000 with no route to container root under normal operation, but `CAP_SYS_ADMIN` plus unconfined seccomp/AppArmor mean that code inside the worker that escalates to root can reach the Docker host (on Docker Desktop, the WSL VM).
On hosts where the daemon cannot start, the entrypoint logs `FOUNDRY_DOCKER_UNAVAILABLE`, leaves `DOCKER_HOST` unset, and the run continues in degraded mode — authoring integration tests that execute where a daemon is available (CI, native Linux).
With the switch off, an implement run gets the plain hardened spec, no `DOCKER_HOST`, and no daemon; the entrypoint skips it silently (no `FOUNDRY_DOCKER_UNAVAILABLE` line).

## Publisher

A short-lived, model-free container that holds the provider token for one worker run ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)). **Prepare** mode builds the run's base bundle before the worker starts; **Publish** mode turns the worker's run bundle into a pushed branch and, on success, an opened or updated pull request. **Probe** mode tells whether a paused provider host can be reached again (see [Host Pause](#host-pause)); **proxy-probe** mode tells whether the Package Source Proxy can be reached again (see [Package Proxy Pause](#package-proxy-pause)).
Its image (`foundry-publisher:local`, `workers/publisher/`) is the official `alpine` image pinned by digest plus `git` — no `claude` CLI and no repository toolchain — built by the [Worker Image Build](#worker-image-build).
It runs under the worker hardening ([ADR 0088](docs/adr/0088-worker-containers-run-hardened-by-default.md)) with no capability added back, as an unprivileged user, with a read-only root filesystem and anonymous volumes at `/work` (its scratch repository), `/tmp` and `/var/tmp`. Git runs in a fresh repository with `core.hooksPath=/dev/null` and no system or global configuration; the token reaches git only through `GIT_ASKPASS`.
**Prepare** fetches the default branch (and, for a Continuation or Revision run, the run's existing branch) and writes `in/base.bundle` to the [Hand-off volume](#hand-off-volume), then reports the run's **base** on one stdout line, `FOUNDRY_BASE default_branch=<name> default_sha=<sha> [branch_sha=<sha>]`, which Foundry persists on the WorkerRun (`base_default_branch_head`, `base_run_branch_head`). Its transfers with the provider form one retried unit, the **provider phase**: `ls-remote`, the ref checks (default branch present, run branch present, review branch present) and the fetch. When `ls-remote` or the fetch fails, `classify_error` reads git's stderr as `unreachable` (git's libcurl transport prefix `unable to access` with no HTTP status or with 429 or a 5xx: DNS failure, refused or timed-out connection, the provider shedding load), `permanent` (the provider refused the credentials or the repository, or git had none to offer) or `transient` (anything else, such as a truncated pack, `early EOF` or the remote hanging up). A `permanent` failure on any attempt stops at once. An `unreachable` or `transient` one re-runs the whole provider phase up to 3 more times, 5 s, 15 s and 40 s apart (`PROVIDER_RETRY_DELAYS`), printing one stderr line per retry that names the classification, the attempt and the sleep, and no sentinel; a retry that succeeds continues as if the first attempt had. When every attempt failed, the **last** attempt's classification chooses the stage: `unreachable` prints the sentinel with stage `unreachable` (`detail=could not reach the repository: <git's last error>`), and the run fails `ProviderUnreachable` (category `provider_unreachable`), which its issue carries into its failed state and which also trips a [Host Pause](#host-pause) for the repository's host, saved with the run's failure; `transient` prints the sentinel with stage `fetch` (`detail=<step>: <git's last error>`, the step being `could not reach the repository` for `ls-remote` and `fetching the repository failed` for the fetch), and the run fails [`FetchFailed`](#fetch-failed) (category `fetch_failed`), which pauses no host. Any other failure (a `permanent` git failure, an empty repository, or a run or review branch missing, including one deleted on the remote between two attempts) prints the bootstrap sentinel with stage `clone`, and the run fails `WorkerBootstrapFailed`. In every case no worker container starts.
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

The checks the [Publisher](#publisher) runs on a [Run bundle](#run-bundle) before pushing (#788): a **secret block** plus **size and binary reverts**. After the identity rewrite it scans the content added by the [Run commits](#run-commits) with gitleaks (pinned by version and checksum in the Publisher image) using Foundry's own configuration (`workers/publisher/gitleaks.toml`); a repository's `.gitleaks.toml`, `.gitleaksignore` and `gitleaks:allow` comments are never honoured. The scanner's rules split into two classes, following GitHub secret scanning, whose push protection covers provider patterns and not passwords ([ADR 0107](docs/adr/0107-generic-secret-patterns-are-published-for-review.md)):

- **Provider pattern** — a gitleaks rule for one service's token format (`github-pat`, `gitlab-pat`, `aws-access-token`, `anthropic-api-key`, Foundry's own `anthropic-oauth-token`, …). A match blocks the push.
- **Generic pattern** — a gitleaks rule for a free-form secret shape, tagged `foundry-generic` in `workers/publisher/gitleaks.toml`, the single source of a rule's class. Today that is only `generic-api-key`.
- **Generic finding** — a match of a generic pattern. It is published, reported, and listed in the pull request. It never blocks and never enters `ConfirmedFindings`.

Any provider-pattern finding pushes nothing: the Publisher reports `FOUNDRY_SECRET_FINDING commit=<sha> line=<n> rule=<id> path=<path>` lines for the provider findings only (never the matched value) and `FOUNDRY_PUBLISH result=secret_detected findings=<n>` counting them, the run becomes `PublishBlockedRun` keeping its Hand-off volume, and its issue waits in Needs you as `publish_blocked` with **Discard run** or **Confirm false positives** (#899). Confirming publishes the run with exactly the confirmed findings suppressed: Foundry passes them to the Publisher as `CONFIRMED_FINDINGS`, one gitleaks fingerprint `<commit>:<path>:<ruleId>:<line>` per line, which `publisher.sh` hands to `--gitleaks-ignore-path`; any other provider finding blocks the run again. Each confirmation adds to the run's `ConfirmedFindings`, `FalsePositivesConfirmedAt` records the latest, and the pull request body or the run's summary comment lists them in a collapsed "Confirmed false positives (n)" section (path, line and rule; never a value), with no line in the warning alert because a person already decided them. A scan whose findings are all generic pushes as usual (#956): every pushing result (`published`, including a run already on its branch, and `diverged`) prints one `FOUNDRY_GENERIC_FINDING commit=<sha> line=<n> rule=<id> path=<path>` line per generic finding, sorted and de-duplicated, before its result line. Foundry keeps them on the run (`GenericFindings`, replaced by each attempt that pushes) counts them in the warning alert that leads the pull request body or the run's summary comment, and lists them in a collapsed "Possible secrets (n)" section (path, line and rule; never the commit or a value) after the reverted files. A generic finding in a file the gate then reverts is still listed, as the revert is a forward commit and the content stays in pushed history. Otherwise every file the Run commits added or changed that is binary by git's detection or larger than 5 MiB is restored, in one code-authored commit `chore: revert files blocked by the Foundry diff gate`, to its content at `<D>` for a rebased tip and at `<base>` otherwise (deleted when it did not exist there), reported as `FOUNDRY_REVERTED_FILE reason=binary|oversized size=<bytes> path=<path>`, kept on the run (`RevertedFiles`), counted in the warning alert that leads the pull request body or the run's summary comment, and listed in its collapsed "Reverted files (n)" section. Deletions and CI/CD files are not gated.

## Provider

A git hosting platform (GitHub, GitLab).
Each provider has its own API client, label format (flat on GitHub, scoped on GitLab), and CLI tooling (`gh`, `glab`).
The provider is carried on `ClaimedIssueDispatch` so the worker dispatch knows which provider the run targets.
No worker holds a provider credential ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)): Foundry sets no `GH_TOKEN`, `GITLAB_TOKEN` or `GIT_PAT`, and the entrypoint unsets any that leaked into its environment. The Account PAT is used only by Foundry itself and by the [Publisher](#publisher), which fetches the base and pushes the run's branch.
The provider CLIs (`gh`, `glab`) are still baked into the worker base image (`Dockerfile.base`), unauthenticated.

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
Deleting an account first removes every repository its Namespace Claims cover (same host, owner resolved by `Credential.ResolveCoveringNamespace`) through `RepositoryRemoval` (see [Removed Repository](#monitored-repository)), all in one call while the account and its token still exist, so each repository's webhooks are deleted with that token; then the account is deleted. The deletion is refused with `409 Conflict` naming the blocking issues, and nothing is removed, while any of those repositories has work in flight.

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
Six variants:

- `Authenticated(AccountName, MissingScopes)` — the token authenticated and the PAT owner resolved; `MissingScopes` lists any required scopes absent from the token.
- `AuthenticationFailed` — the token was rejected (401/unauthorized).
- `ScopesUnverifiable(AccountName)` — the token authenticated and the owner resolved, but the token's scopes could not be read. Only GitLab produces it: group and project access tokens, for which `personal_access_tokens/self` returns non-2xx. GitHub never returns it. GitLab tokens need the `api` scope from the provider's [permission list](#repository-eligibility); the GitLab Maintainer role is verified per repository by the eligibility probe.
- `WrongTokenKind(AccountName, WrongKind)` — the token authenticated and the owner resolved, but it is a kind of token Foundry refuses whatever it holds. A GitHub token must be a fine-grained personal access token (`github_pat_` prefix); any other GitHub token — classic `ghp_`, OAuth `gho_`, a GHES classic token, any other prefix — is `WrongTokenKind` with `WrongKind = github-classic`, and Foundry reads no scopes from it. A fine-grained token is `Authenticated` with empty `MissingScopes`: its permissions are verified per repository by the eligibility probes (`IGitHubWriteProber`), and the create-time probe rejects a token missing any of them, naming every missing permission. GitHub cannot report a fine-grained token's permissions, so extra permissions and "All repositories" access are not checked.
- `IdentityUnresolved` — the response could not be parsed to an identity (neither provider's identity field present).
- `ProviderMismatch(DetectedProvider)` — the response carried the *other* provider's identity shape (e.g. GitLab answered while GitHub was selected); names the detected provider.

Three behavioral rules apply:

1. A successful authentication requires a PAT owner — a "valid but nameless" state cannot be represented; if the owner cannot be resolved the outcome is `IdentityUnresolved` or `ProviderMismatch`, not `Authenticated`.
2. `ScopesUnverifiable` warns but permits saving — the provider owns authorization and claim-time scope enforcement is the real gate, so an unreadable scope list is advisory, not blocking.
3. `WrongTokenKind` blocks saving: account creation and token replacement refuse it with a 400 problem (`Credential.WrongTokenKind`). A saved GitHub token of the wrong kind makes every repository of its account `Denied` with no missing permissions and no write probe sent, and the account summary's `NonConformingToken.WrongKind` names the kind; replacing the token clears it through the credential-update re-evaluation, and the Denied re-probe cooldown is the automatic exit.

The endpoint (`POST /api/accounts/validate-token`) maps these variants to the response `Kind` field: `"authenticated"`, `"authenticationFailed"`, `"scopesUnverifiable"`, `"wrongTokenKind"` (with `wrongKind`), `"identityUnresolved"`, `"providerMismatch"`.

### Unreadable Token

A stored `accounts.token` value that cannot be decrypted — the Data Protection key that encrypted it was lost or rotated (`CryptographicException`), or the stored ciphertext is not valid base64 (`FormatException`).
Modeled as the `Unreadable` variant of the `ProviderToken` value object (`Present(Value)` / `Unreadable`), materialized by `ProviderTokenConverter` when decryption fails — never an empty-string sentinel.
An unreadable token is distinct from an **absent** token: the token column is null when no token is configured, and `Unreadable` when a token exists but cannot be read. Both report `HasToken = true` only for `Unreadable` (a value is present); the finer state is carried by the `TokenStatus` field (`"present"` / `"absent"` / `"unreadable"`).
An account whose token is `Unreadable` sends no provider request: eligibility evaluation and every polling path short-circuit before calling the provider, so the account cannot silently degrade to anonymous access. Affected repositories report the credential-specific reason `credential-unreadable` rather than masquerading as inaccessible or unpushable.
Materialization logs a warning naming `accounts.token` and the credential id. The state is repaired by re-entering the token through the account update path — the degraded credential loads normally and the re-entry never reads the old token, so no manual database edit is required.

### Invalid Token

An account token the provider refused with HTTP 401 on a runtime call. In RFC 6750 §3.1 terms (`invalid_token`) it is expired, revoked, malformed or otherwise invalid; a 401 cannot tell these apart. The provider decides it, unlike an [Unreadable Token](#unreadable-token), which Foundry itself cannot decrypt. A 403 (rate limit, SAML, permission) is not an Invalid Token.
Modeled as `Credential.TokenValidity` — `Valid`, or `Invalid(DetectedAt, LastCheckedAt)` — persisted in the nullable `accounts.token_invalid_since` / `token_checked_at` columns (both null means `Valid`). A provider client reports a 401 as `ErrorKind.Unauthorized`.
**Confirm, then mark:** when a repository's issue listing (scheduled or nudged) fails with `Unauthorized`, Foundry checks the token once with the same test as the account form's live token check (`ValidateTokenAsync`). Only `AuthenticationFailed` marks the account Invalid (`MarkTokenInvalid`); an authenticating outcome (`Authenticated` or `ScopesUnverifiable`) leaves it Valid, and a check with no verdict (transport error, 5xx) changes nothing, so the next cycle runs the same path. The repository's eligibility is evaluated again in the same cycle.
While Invalid, `TokenUsability` is `Invalid`, so no regular provider call uses the token: covered repositories are Ineligible with `credential-invalid`, are not dispatched, and their webhook nudges and post-exit queries fail with `CredentialTokenInvalid`. When the token is also Unreadable, it is reported as Unreadable. The account summary reports `TokenStatus = "invalid"` and `TokenInvalidSince`.
**Recheck:** once per `Credential.TokenRecheckCooldown` (15 minutes) per account, the poll loop checks the stored token again. An authenticating outcome returns the account to `Valid` (`MarkTokenValid`) and the poll runs that cycle; `AuthenticationFailed` stamps `LastCheckedAt`; no verdict stamps nothing, so the recheck runs again next cycle. Only one check per interval reaches the provider, so a dead token cannot trip the provider's failed-login lockout for sibling accounts with the same login.
**Rotation:** setting a new account token (account creation, or the update path with a token) returns the account to `Valid` at once and increments `Credential.TokenVersion`, the account's concurrency token. A verdict about a token that was replaced while it was being checked fails the version check and is dropped without retry; the next cycle tests the new token.

### Token Expiry

The account token's expiry as last read from the provider when the token was set. Modeled as `Credential.TokenExpiry`: `NotChecked` (Foundry has not read it), `Unavailable` (the provider reported none), `Never` (the token never expires) or `On(ExpiresAt)`. It changes only together with the token: `Credential.SetAccountToken` takes an `IssuedToken(Value, Expiry)`, so a token cannot be set without stating its expiry, and an account with no token is `NotChecked`. Persisted in `accounts.token_expiry_kind` / `token_expires_at`; accounts saved before tracking read as `NotChecked`.
`TokenExpiry.StatusAt(now)` judges it: `Expired` once `now >= ExpiresAt`, `ExpiringSoon` within `TokenExpiry.WarningWindow` (14 days, inclusive), otherwise `Current`. The server computes the status; the client only renders it. Expiry never blocks saving.
GitLab reads it from `expires_at` on `GET /personal_access_tokens/self` (a date maps to midnight UTC at its start, erring a day early; `null` is `Never`; a non-2xx response, as for group and project access tokens, is `Unavailable`). GitHub accounts store `NotChecked` until the expiry is read from GitHub.
An [Invalid Token](#invalid-token), unreadable or absent token shows no expiry: the provider's verdict wins.

## Namespace Claim

The exclusive association between an Account and an owner namespace on a host — stored in `credential_namespaces` with a unique `(host, namespace)` constraint, so each namespace is served by exactly one account.
Claims are derived from the token's writable-repository listing (every distinct owner of a repo the token can push to) at account creation, token rotation, and repository recheck.
A Monitored Repository carries no account reference; on each eligibility evaluation the covering account is resolved by matching the repository's owner against claims (`ICredentialResolver`). A repository whose owner no account claims evaluates as Ineligible (`no-credential:<namespace>`). Token rotation and repository recheck re-evaluate the repositories whose claims they drop; the poll records a repository no account covers as Ineligible (`no-credential:<namespace>`) and skips it. A repository no account covers is an [Unclaimed Repository](#monitored-repository), listed apart from every account-scoped repository listing.

## Global Settings

A strongly-typed single-row entity storing all UI-configurable settings.
Includes worker settings (max concurrent, timeout), the Worker Image Configuration (image tag and build toggle), the nested-Docker switch, and Image Build State, the Worker Resource Limits (memory MB, CPU count, PID ceiling), the Claude Code Settings (additional deny rules; the model is a per-role setting, see Worker Role), the credit-probe interval (`ProbeIntervalMinutes`, default 60, validated to a minimum of 5), and the usage-limit auto-resume preference (`AutoResumeOnUsageReset`, edited with the probe and poll intervals by `PUT /api/settings/dispatch`; Workers reads it through `IGlobalSettingsQueries.GetAutoResumeOnUsageResetAsync`). The runtime dispatch-pause state is not Global Settings: it is the Workers-owned [Dispatch Pause](#dispatch-pause) row ([ADR 0118](docs/adr/0118-dispatch-pause-state-is-owned-by-the-workers-module.md)). `GET /api/settings` still returns its fields (`usageLimitResetsAt`, `isDispatchPaused`, `pausedHosts`, `packageProxyPause`), which Settings fills from `IDispatchPauseQueries` in `Workers.Contracts`.
Worker prompts are not settings: they ship in the worker image with each Worker Role (see Worker Role).
Additional deny rules are appended after the always-enforced base deny list.
The auth-invalid pause is not stored here — it is `ClaudeAccount.Validity` in the Credentials module (see Worker Authentication).
`ProbeIntervalMinutes` governs how long the credit block waits between probes (see Credit Probe); the operator sets it in the dispatch-settings form, `GlobalSettings.UpdateProbeInterval` rejects a below-minimum value, and the credit-probe scheduler reads it via `IGlobalSettingsQueries.GetProbeIntervalMinutesAsync` at each arm.
DB is the single source of truth — it is the only source for worker settings; `IConfiguration` is not consulted for any setting the UI manages, and the `Workers` configuration section no longer exists.
Webhook ingress settings ([ADR 0085](docs/adr/0085-webhooks-nudge-the-poll-loop.md), [ADR 0116](docs/adr/0116-webhook-ingress-is-required-and-verified-by-a-self-probe.md)): the required **Webhook Public Base URL** (`WebhookPublicBaseUrl`) and the [Backstop Interval](#backstop-interval) (`BackstopIntervalMinutes`, default 15, 5..1440, `Settings.InvalidBackstopInterval`), both edited in the setup wizard or the dispatch settings page through `PUT /api/settings/webhooks`. A fresh, never-saved settings row has no URL; once set, the URL can be changed but never cleared (a blank value is rejected with `Settings.InvalidWebhookPublicBaseUrl`). The URL must be an absolute `https` URL without query, fragment or userinfo, at most 2048 characters, whose host is not `localhost` or a literal loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`, `fe80::/10`) or unspecified (`0.0.0.0`, `::`) address — rejected otherwise with `Settings.InvalidWebhookPublicBaseUrl`; private ranges and every other DNS name are allowed, and the host is never resolved. **Reachability check:** a URL that differs from the stored one is saved only when a self-probe through it reaches this Foundry process — a one-time 256-bit code sent as `GET <URL>/webhooks/probe/<code>` must answer 2xx *and* arrive at this process within 10 seconds; otherwise the save is rejected with `Settings.WebhookPublicBaseUrlUnreachable` (cause `dns`, `tls`, `timeout`, `other-instance` or `http-status`), nothing is saved and no `WebhookPublicBaseUrlChanged` is raised. Saving the stored URL again (for example to change only the Backstop Interval) makes no probe. The webhook port itself is host configuration (`Webhooks:Port`, default 5099), not a Global Setting.

## Monitor

The background process that polls configured repositories for issues labeled `foundry`.
Runs on a fixed tick interval (30s default) and checks whether each repo is due for polling based on its scheduled interval — the [Backstop Interval](#backstop-interval) while its [Webhook Status](#webhook-status) is `Active` and webhook ingress is configured, otherwise its effective poll interval (falling back to the global default when unset) — and its `LastPolledAt` timestamp.
It is also the only consumer of the [Nudge](#nudge) channel: a nudge wakes it immediately, and nudged passes run in the same loop as scheduled polls, so a repository is never polled concurrently. A scheduled poll composes the same per-pass `RepositoryPoller` entry points a nudge runs (listing, dependency, review, checks, branch-rules step), so behaviour is identical whether a pass is nudged or scheduled.
On the first scheduled tick after process start (clean restart or crash) it runs a one-time **startup catch-up**: every active repository is polled once as a scheduled poll (listing, dependency, review, checks and branch-rules passes, plus the rebase step backstop) regardless of `LastPolledAt`, because webhook deliveries that arrived while Foundry was down are rejected and never redelivered. The catch-up flag is in-memory and consumed by that tick, so a repository whose catch-up poll fails returns to its normal schedule, and a change the catch-up finds is never a [Missed Change](#missed-change). Later ticks poll only repositories that are due.
After the per-repository passes, the scheduled poll runs the **webhook registration step** for each polled repository (every active repository on the startup catch-up) in its own scope: it registers a repository whose [Webhook Spec Stamp](#webhook-spec-stamp) is stale (see [Webhook Registration](#webhook-registration)). With a current stamp it makes no provider call, so it adds nothing to the per-cycle call budget; one repository's failure is logged and never stops the rest.
The per-cycle provider call cost is bounded by `RepositoryPoller.MaxFixedPollCallsPerCycle` (see [Provider Rate Budget](#provider-rate-budget)) and enforced by the poll-call invariance test; the nudge rate cap on event-triggered runs is enforced by the sibling nudge-call-cap test; deadline nudges run at their deadline outside that cap, and each deadline producer bounds its own re-arming.

### Webhook Delivery

One verified inbound HTTP request from a provider (see [ADR 0085](docs/adr/0085-webhooks-nudge-the-poll-loop.md)). Deliveries arrive only on the dedicated webhook port (always bound; `Webhooks:Port` overrides the default 5099), at `/webhooks/github/{hookId}` or `/webhooks/gitlab/{hookId}`. The same port also serves the reachability probe path `/webhooks/probe/{code}` (204 for a code pending in this process, 404 for any other). A two-way port guard returns 404 for any other path on that port and for `/webhooks/*` on the main port. Verification fails fast before the body is parsed: an unknown hook id is 404; a missing or mismatched secret is 401 — GitHub's `X-Hub-Signature-256` (HMAC-SHA256 of the raw body under the repository's secret) or GitLab's `X-Gitlab-Token` (the plain secret token), both compared in constant time; and a body naming another repository (GitHub `repository.full_name`, GitLab `project.path_with_namespace` including nested groups, case-insensitive) is 400. A verified delivery returns 202 without calling the provider, after enqueueing at most one Nudge. Deliveries touching nothing Foundry tracks are dropped (the relevance filter).

### Webhook Status

Per-repository webhook health: `NotConfigured` (no public base URL stored yet — derived, never stored) / `NotRegistered` / `Pending` / `Active` / `Silent`. Only `Active` uses the Backstop Interval. The operator's "I've added the webhook" action and a successful [Webhook Registration](#webhook-registration) set `Pending` (an `Active` hook stays `Active`) — a registration also triggers the provider's test delivery, which normally activates the hook within seconds; any verified delivery (including GitHub's `ping`; GitLab has none, so a GitLab hook added by hand stays `Pending` until its first delivery — typically the hook form's "Test" action) moves `Pending` or `Silent` to `Active`; a [Missed Change](#missed-change) moves `Active` to `Silent`.

### Webhook Registration

Foundry creating or updating a repository's provider webhook itself (`WebhookRegistrar`), with the Account token, which needs GitHub **Webhooks: read and write** or the GitLab **Maintainer** role ([ADR 0115](docs/adr/0115-one-account-token-verified-against-every-permission-foundry-uses.md)). A provider hook belongs to the repository when its URL is an absolute http(s) URI whose path ends, ordinal, with `/webhooks/github/{hookId}` or `/webhooks/gitlab/{hookId}` — the repository's 128-bit random hook id; the base URL is ignored, so a hook at any earlier public base URL or one added by hand is adopted, and another install's hook never matches ([ADR 0116](docs/adr/0116-webhook-ingress-is-required-and-verified-by-a-self-probe.md)). With no matching hook one is created; otherwise one match is updated in place — preferring an exact payload-URL match, then the first listed — with URL, secret, JSON content type, exactly the mapped event list (GitHub events; GitLab trigger flags, push events included and every other trigger, tag pushes among them, explicitly off) and the active flag, and every other match is deleted (GitHub `POST/PATCH/DELETE /repos/{o}/{r}/hooks`, GitLab `POST/PUT/DELETE /projects/:id/hooks`). A failed delete of a duplicate is logged and does not fail the registration. Only the first page of 100 hooks is read.
Runs when a repository is added; on the repository page's "Register webhook" action (`POST /api/accounts/{accountId}/repositories/{id}/webhook/register`); when the Webhook Public Base URL changes to a new value (`WebhookPublicBaseUrlChanged`, every repository); once on each transition into `Silent` — a hook still `Silent` afterwards is not retried by this trigger until its next transition into `Silent`; and from the scheduled poll's **stale-stamp step** for every polled repository whose [Webhook Spec Stamp](#webhook-spec-stamp) is not the current one (never registered counts as stale), unless its Write Probe verdict is `Denied` or its last registration failed less than an hour ago. So a change to the subscribed events reaches every hook on the next start, and a repository whose token becomes usable (a rotated account token, a verdict turning `Granted`) is registered on a later poll without a dedicated trigger. Multi-repository triggers run sequentially and one failure never stops the rest.
Outcome (returned; only its effect on the repository is persisted): `created` / `updated` (status becomes `Pending`), `skipped` (`not-configured`, or `token-unreadable` when the account token is absent or unreadable — no provider call) or `failed` (`insufficient-permission` for GitHub 403/404 on `/hooks` or GitLab 403; `provider-error` otherwise). After every successful create or update Foundry asks the provider for its test delivery with the same token — GitHub `POST /repos/{o}/{r}/hooks/{hook_id}/pings`, GitLab `POST /projects/:id/hooks/:hook_id/test/push_events` — addressed by the provider hook id the upsert returned, so the hook turns `Active` when that delivery arrives; a failed test (any status, including GitLab's limit of five test requests per minute, or an exception) is logged at warning level and changes neither the outcome, the status nor the stamp. A success stores the current Webhook Spec Stamp and clears the failure time; a failure records its time (`WebhookRegistrationFailedAt`) and leaves the stamp unchanged; a skip records nothing. Skipped or failed leaves the status unchanged, so a `NotRegistered` repository keeps showing the manual instructions; polling is never blocked.
Removing a repository deletes every hook matching its hook id with the covering account's token, before the removal's transaction. No covering account, an unreadable token, a refused (GitHub 403/404, GitLab 403) or failed provider call is logged and the repository is still removed; a hook left behind delivers to an unknown hook id and is answered 404.

### Webhook Spec Stamp

A lowercase hex SHA-256 over a canonical, ordered text of the provider (`github` / `gitlab`), the hook settings Foundry sends — taken from the same definition the registration payload is built from (`WebhookHookSettings`): GitHub's subscribed events, content type `json`, `insecure_ssl` `0` and `active` true; GitLab's every trigger with its on/off value and `enable_ssl_verification` true — and the Webhook Public Base URL. It never includes the secret. A successful [Webhook Registration](#webhook-registration) stores it on the repository (`WebhookSpecStamp`); a repository whose stored stamp differs from the current one — never registered, registered before the event list changed, or at an older base URL — is stale. Adding or removing a subscribed event changes the stamp with no hand-maintained version. The operator's "I've added the webhook" action stores no stamp, so the poll still registers (adopts) a hand-added hook.

### Nudge

A request to run one poll pass for `(repository, pass, issue?)` now — or not before a deadline. It is caused by a relevant delivery or a deadline, coalesced (a ~2 s window; pending nudges for one key run once) and rate-capped (each key runs event-triggered nudges at most once per the repository's normal effective poll interval: a leading run, then one coalesced trailing run at the end of that interval), and never changes domain state itself. A not-before (deadline) nudge runs at its deadline, outside the rate cap: it neither waits out the cap window nor stamps it, and a capped event-triggered nudge pending for the same key runs with it. Every deadline producer must therefore be self-bounded (a finite number of deadlines per episode or batch); the [Provider Rate Budget](#provider-rate-budget) still caps total calls underneath. Nudges live in a bounded in-memory channel; a full channel drops the new nudge and a restart loses pending ones — the backstop poll recovers both. Event mapping: `issues` → listing, except that an `issues` `edited` delivery for an issue awaiting input on its own thread nudges that issue's awaiting-input pass (the edit may be the answer), and an `issues` close of a blocker of a tracked issue nudges the listing plus dependency pass even when the blocker is neither tracked nor labeled (GitHub sends no `issue_dependencies` delivery when a blocker closes); `issue_dependencies` → listing + dependency pass; `pull_request`, `pull_request_review`, `pull_request_review_comment`, `issue_comment` (on a PR) → review pass for the owning issue (checks pass when it is checks-pending); `check_suite` (completed), `status` → checks pass; `branch_protection_rule`, `repository_ruleset` → branch-rules step; `push` → the awaiting-input pass of the awaiting-input issue whose resume branch it moved (`refs/heads/<branch>`; the push may be the answer), otherwise none; `ping` → none. GitLab events are read from the body's `object_kind` and nudge the same passes: `issue` / `work_item` → listing (plus the dependency pass when the closing issue blocks a tracked issue; an `object_attributes.action` `update` for an issue awaiting input on its own thread nudges its awaiting-input pass instead — GitLab sends `update` for label changes too, and the pass's body read decides); `merge_request`, `note` (on a merge request) → review pass for the owning issue (checks pass when it is checks-pending); `pipeline` (finished status) → checks pass; `push` → as for GitHub; Foundry registers the hook with push events on (tag pushes off). Confidential issues and comments map like their public counterparts. GitLab has no event for issue links or protected branches, so it never nudges the dependency pass otherwise or the branch-rules step. An issue entering checks-pending raises `IssueEnteredChecksPending`, which schedules a checks nudge immediately plus not-before nudges at the grace-window and upper-bound deadlines. An issue entering rebase-pending raises `IssueEnteredRebasePending`, which nudges the rebase pass immediately; the rebase step schedules its own not-before nudges while it waits for a rebase to land or backs off a transient failure.

### Backstop Interval

The poll interval for repositories whose webhook is `Active` (Global Settings, default 15 minutes) — never shorter than the repository's own effective poll interval.

### Missed Change

A change found by a **scheduled** poll (other than the startup catch-up, see [Monitor](#monitor)), in a category the provider has a webhook event for, that no delivery announced. The categories are issue detection, detail change or untrack; a closed issue or pull request or new review feedback; a provider-driven checks verdict; and — on GitHub only — a branch-rule change (the `Eligible`/`Ineligible` outcome of the default branch's rules; transitions through `Unreachable`, a denied write probe or a missing credential do not count). It moves `Active` to `Silent`, so the next poll uses the normal interval. Changes without a mapped event on that provider — time-based checks exits, dependency refreshes, blockers in another repository, and GitLab protected-branch changes — never count; while `Active` they are picked up within the Backstop Interval.

## Package Source

A remote NuGet source declared under `<packageSources>` in a repository's root `nuget.config`, other than nuget.org, identified by its service-index URL ([ADR 0112](docs/adr/0112-workers-restore-private-nuget-package-sources-through-a-foundry-proxy.md)).
Read by the parser (`NuGetConfigParser`) from every `<add key value>` whose `value` is an absolute `http`/`https` URL on a host other than `api.nuget.org`, minus every key listed with `value="true"` under `<disabledPackageSources>`; a local folder (`./packages`) or an environment-variable value (`%FEED_URL%`) is not one. A `<clear />` drops the sources declared before it.
Each poll lists the repository's root directory on its default branch once (GitHub `git/trees/HEAD` with the previous listing's ETag, so an unchanged root is a 304 that does not count against the primary limit; GitLab `repository/tree`, paged by 100). The root config is the one file entry named `nuget.config` ignoring case — a directory, symlink or submodule of that name is absent — and is read only when its blob SHA differs from the stored one. The [Monitored Repository](#monitored-repository) records the result in one call (`RecordRootListing`): its declared Package Sources, the config's raw text and blob SHA, the listing ETag, or — when the file is not well-formed XML, or two root files differ only in case (`more than one root nuget.config`) — the root config as **invalid** with a message instead of a source list. No root config records an empty list. A failed listing or file read leaves the stored state as it was and is logged; it never clears the declared sources. A nested `nuget.config` is not read.

### Package Source Probe Verdict

Whether Foundry can read a Package Source with one credential, held on the `PackageSource` aggregate (keyed by its service-index URL) as one verdict per credential it was probed with, plus the time of that probe: `Unchecked` (never probed with that credential), `Readable`, or `Unreadable(httpStatus?, reason)` — `httpStatus` is null for a transport failure, and `reason` is one line such as `service index returned 401`, `not a NuGet v3 service index` or `search returned 403` ([ADR 0112](docs/adr/0112-workers-restore-private-nuget-package-sources-through-a-foundry-proxy.md)).
The **probe** is `GET <service-index URL>`, which must answer 2xx with a JSON body whose `resources` hold an entry with an `@type` starting `SearchQueryService`, then `GET <that @id>?q=&take=1`, which must answer 2xx. Every request Foundry sends to a Package Source goes through `PackageSourceUpstreamClient`: `GET`/`HEAD` only, Basic auth with the credential's username and token, redirects followed on the server side with the credential sent only to the source URL's origin.
A registered [Package Source Credential](#package-source-credential) comes first. Without one, the credential is the account that covers the declaring repository's namespace (`ICredentialResolver`) **only when the source's host equals the repository's provider host**; otherwise the source is probed with no credential, so an account token never reaches another host. Repositories under two accounts judge one source by their own account's verdict.
Probes run in the monitor's scheduled tick, before the polls and outside `RepositoryPoller.PollAsync` (so they are not part of the [Fixed Per-Cycle Cost](#fixed-per-cycle-cost)), once per source and credential however many repositories declare it. Cadence: at once for a source-and-credential pair never probed (a newly declared source) or whose account token was replaced since; a `Readable` verdict after 30 minutes; an `Unreadable` one after a 15-minute cooldown (the [Invalid Token](#invalid-token) recheck cooldown). A repository's Re-check probes its declared sources at once, bypassing the cadence, before evaluating eligibility. The poll's eligibility evaluation only reads stored verdicts.

### Package Source Proxy

The WebApi port that serves Package Sources to live runs, because workers hold no provider credential ([ADR 0112](docs/adr/0112-workers-restore-private-nuget-package-sources-through-a-foundry-proxy.md), [ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)). Always on, on `PackageSources:ProxyPort` (default 5287), bound on all interfaces (`http://*:<port>`) so worker containers reach it through `host.docker.internal`. It is an entry in the ingress port guard ([ADR 0085](docs/adr/0085-webhooks-nudge-the-poll-loop.md)): the proxy port serves only `/package-sources/*`, and no other port serves that prefix.
A request is `GET`/`HEAD /package-sources/<sourceId>/<upstream path>`, where `sourceId` is the `PackageSource` aggregate's id; any other method is 405. It needs Basic credentials whose password is a live run's [Run Source Token](#run-source-token) (the username is ignored), for a source that run's repository declares; anything else is a 401 carrying `WWW-Authenticate: Basic realm="foundry-package-sources"`, the challenge NuGet waits for before it sends credentials. An allowed request goes through `PackageSourceUpstreamClient` to `<source origin>/<upstream path>?<query>` with the credential the [probe](#package-source-probe-verdict) uses for the run's repository, and only when the upstream path lies under the service-index URL's directory (404 otherwise), so the account token never reaches another API path on the provider host. The one exception is a **GitLab group feed** (a service-index path ending in `/api/v4/groups/{id}/-/packages/nuget/index.json`; its **API prefix** is the path before `/groups/`): its service index has no download resource, and each registration entry's `packageContent` is a project-level URL, `<API prefix>/projects/{projectId}/packages/nuget/download/{name}/{version}/{file}` on the source's origin. The proxy re-points those URLs in response bodies too, and forwards a `GET`/`HEAD` for one only after confirming it against the feed's own registration index, `<RegistrationsBaseUrl>/{lowercased name}/index.json` (following page `@id`s when a page carries no inline items), read with the same credential: the download goes upstream only when some `packageContent` equals the requested URL, path and query; an unlisted download, or a registration that is not JSON or exceeds the response buffer limit, is 404, and a registration that cannot be read is 502 (an upstream 401 is the "refused Foundry's credential" 502). The run token so reaches only packages the feed itself lists.

Upstream statuses pass through, except an upstream 401, which becomes a 502 with a one-line body naming the source, so NuGet does not read it as a challenge to the worker. A JSON response (`application/json` or `*+json`) has every absolute URL on the source's origin under the service-index directory re-pointed at `http://host.docker.internal:<port>/package-sources/<sourceId>/<path and query>`, because NuGet v3 service indexes and registrations carry absolute URLs; every other response, `.nupkg` downloads included, streams through unbuffered. The proxy caches nothing.
Workers reach it through the `RestoreConfigFile` redirect (see [Worker](#worker)): the generated config points each declared source at `http://host.docker.internal:<port>/package-sources/<sourceId>/<service-index path>` with `allowInsecureConnections="true"`, and gives it the Basic credentials `foundry-run` / the run's token.

### Run Source Token

A random secret every worker run mints when it begins (`StartingRun.Begin`; 32 bytes from a cryptographic RNG, base64url), which the run's worker presents to the [Package Source Proxy](#package-source-proxy) as its Basic password. The run persists only its SHA-256 hash (`worker_runs.source_token_hash`) and keeps it through every state; the token itself is readable only from the begun run (`StartingRun.SourceToken`) and is never persisted or logged. It is not a provider credential.
A token passes only while its run is `starting` or `active` (`IRunSourceTokenQueries`), so it stops working when the run's `claude` exits — no revocation step. A run with no declared sources still mints one; two runs of one repository each hold their own.

### Package Source Credential

A username and token the operator registers for one [Package Source](#package-source) in Settings → Package sources, held on the `PackageSource` aggregate and changed only through `SetCredential(username, token, now)` and `RemoveCredential()` ([ADR 0112](docs/adr/0112-workers-restore-private-nuget-package-sources-through-a-foundry-proxy.md)). Both fields are required (empty is missing; whitespace is kept), at most 255 and 2048 characters. The token is encrypted at rest with ASP.NET Core Data Protection under its own purpose and is never returned by the API.
It **takes precedence** over the account credential for every repository that declares the source, whatever the source's host, so one probe verdict (under the `registered` probe credential) judges them all; removing it falls each repository back to its account or to no credential. A verdict counts only for the credential registered now: replacing or removing it re-probes the source at once, outside the request, and a probe of an earlier credential is dropped. A token that can no longer be decrypted makes the source `Unreadable` rather than falling back. The credential stays when no repository declares the source any more, so declaring it again restores it.
The Settings list shows each source's **status**: the registered credential's verdict when there is one; otherwise, over the declaring repositories' own credentials (accounts by name, ordinal, then no credential), `unreachable` when any verdict is, `unchecked` while any is, else `reachableViaAccount` for the first account or `reachableAnonymously`. After each probe a credential change starts, and after a scheduled probe that changes a verdict, the server sends the `package-source` system notification so open Settings pages read the list again.

## Issue

A provider-side issue tagged for Foundry processing.
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `TriageQueuedIssue`, `TriageBlockedIssue`, `TriageInProgressIssue`, `TriageFailedIssue`, `BlockedIssue`, `FreshQueuedIssue`, `ContinuationQueuedIssue`, `RevisionQueuedIssue`, `InProgressIssue`, `RevisionInProgressIssue`, `RebasePendingIssue`, `ChecksPendingIssue`, `MergePendingIssue`, `ReviewIssue`, `AwaitingInputIssue`, `UnchangedIssue`, `CompletedIssue`, `FailedIssue`, `ContinuableFailedIssue`, `RevisionFailedIssue`, `PublishFailedIssue`, `PublishBlockedIssue`, `BranchDivergedIssue`).
Every variant carries the shared properties, including the append-only [clarification log](#clarification) and the latest [Triage Verdict](#triage-verdict), through every transition.
Every new issue is triaged once before its first implement run, and only once its blockers close: `Detected → (TriageBlocked →) TriageQueued → TriageInProgress →` the dependency check (`FreshQueued` or `Blocked`) or `AwaitingInput` ([ADR 0097](docs/adr/0097-every-new-issue-is-triaged-before-dispatch.md), ordering amended by [ADR 0102](docs/adr/0102-triage-runs-once-blockers-clear.md)). An issue that goes back to triage holding [Open Work](#open-work) leaves the dependency check as a `SpecChanged` revision on its branch instead (`RevisionQueued` or `RevisionBlocked`).

Every variant also carries the [Triaged Spec Hash](#triaged-spec-hash) and [Open Work](#open-work).
State transitions are methods on each variant that return the next variant type, enforcing valid transitions at compile time.

### Issue Lifecycle Partition

The lifecycle splits into two partitions defined by `IssueStateRegistry` (server) and mirrored in `ACTIVE_STATES` / `RESOLVED_STATES` (frontend):

- **Active** — every state except `completed`.
- **Resolved** — `completed` only.

The dashboard shows each issue in one of four display groups. They come from the placement the server declares ([Issue Stage](#issue-stage), [Needs action](#needs-action), [Awaits dispatch](#awaits-dispatch)), not from a frontend list of states:

- **Needs you** — Active with [Needs action](#needs-action): it progresses only after a person acts (`review`, `awaiting_input`, `unchanged`, `failed`, `continuable_failed`, `revision_failed`, `triage_failed`, `publish_failed`, `publish_blocked`, `branch_diverged`). On the board (`/`) it sits in the Needs you band, first on the page, as a row whose [lifecycle rail](#lifecycle-step) shows where it stopped. A `review` issue whose checks are green (not Checks red or CI couldn't run) has finished its work and only awaits a person's review or merge, so it shows instead as a one-line row with a lifecycle bar and a link to review the pull request, in a list under those full rows, longest waiting first.
- **Queued** — Active, [Awaits dispatch](#awaits-dispatch) and no Needs action: waiting for a worker run to be dispatched (`detected`, `triage_queued`, `triage_blocked`, `queued`, `blocked`, `revision_queued`, `revision_blocked`, `continuation_queued`, `continuation_blocked`, `review_queued`). It sits in the board's Pipeline band in global dispatch order, or in the Blocked band when it [Waits on blockers](#waits-on-blockers).
- **In flow** — every other Active issue: a worker is running (`in_progress`, `revision_in_progress`, `triage_in_progress`, `review_in_progress`), a review run waits for its automatic retry (`review_failed`), or Foundry's own code is on a [Code step](#code-step) (`rebase_pending`, `checks_pending`, `merge_pending`). It sits in the board's In progress band as a row with its [lifecycle rail](#lifecycle-step); a row whose Claude step runs also shows the worker's live activity. A `review_failed` issue has no worker or step, so the board shows it in Needs you as a warning row ("Review failed") until its automatic retry queues the review again.
- **Resolved** — stage Done (`completed`). It leaves the board and is listed on Resolved history (`/issues`), newest first, next to the run statistics.

A few states deserve explicit note:

- `unchanged` needs action because the worker produced no changes and the user must decide whether to retry — it cannot resolve itself.
- `blocked` is Queued because a `BlockedIssue` auto-transitions to `FreshQueuedIssue` when its blockers close on the provider; no user action is required.
- `triage_blocked` is Queued for the same reason: a `TriageBlockedIssue` auto-transitions to `TriageQueuedIssue` when its blockers close.
- `awaiting_input` needs action (label "Awaiting input") because it advances only when a person answers the posted questions. Its column follows its resume target ([Issue Stage](#issue-stage)).
- `triage_failed` needs action (label "Triage failed"): a transient failure retries itself, anything else waits for Retry triage or Skip triage ([ADR 0014](docs/adr/0014-remove-immediate-requeue-always-pause.md)).

### Issue Stage

The part of the lifecycle that currently owns an issue — **Triage**, **Implement**, **Review** (the review role is judging a green pull request: `review_queued`, `review_in_progress`, `review_failed`, [ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)), **Merge** (the final stage: an open pull request waiting for a person, the state `review` ("Your review"), or Foundry merging it, `merge_pending`), or **Done**. The stage **Review** holds the reviewer's run; the state `review` stays in stage Merge. Declared by each state type (`Issue.Stage`), so a new state cannot compile without one. `awaiting_input` follows its resume target: a re-triage is in Triage, every other target in Implement. A completed issue leaves the board (`/`). Served on `IssueSummary.Stage` as `triage | implement | review | merge | done`; `review` is served only for the review-step states, which no issue reaches while [Review Pull Requests](#review-pull-requests) is off.

### Needs action

True when the issue progresses only after a person acts (`Issue.NeedsAction`). A Needs-action issue is in the Needs you display group: it sits in the Needs you band at the top of the board, whatever its other facts, as a row whose lifecycle rail shows the step it stopped on. A `review` issue with green checks (PR ready, Review skipped or Merge skipped) shows as a one-line row with a lifecycle bar instead, under the Needs you cards; the band's count includes it.

### Awaits dispatch

True when the issue is waiting for a worker run to be dispatched — queued, or detected or blocked before it can be queued (`detected`, `triage_queued`, `triage_blocked`, `queued`, `blocked`, `revision_queued`, `revision_blocked`, `continuation_queued`, `continuation_blocked`, `review_queued`). An awaiting-dispatch issue sits in the board's Pipeline band, one list in global dispatch order across stages, unless it [Waits on blockers](#waits-on-blockers): then it sits in the Blocked band.

### Waits on blockers

True when the issue waits for its open blockers to close before it can move on: `blocked` (triaged or skipped triage, then found blocked), `triage_blocked` (waiting untriaged), and `revision_blocked` and `continuation_blocked` (a queued revision or continuation held by open blockers). Declared by each state type (`Issue.WaitsOnBlockers`), so a new state cannot compile without one; every other state is false. Every blocked state also [Await dispatch](#awaits-dispatch); a client placing an issue gives Waits on blockers precedence. Served on `IssueSummary.WaitsOnBlockers`.

### Entered state at

When the issue entered its current state (`Issue.EnteredStateAt`): the detection time for a `detected` issue, otherwise the time of the transition that created the current state instance. Every transition stamps it anew, a re-queue into the same state type included, and it is never carried over from the state being left. Each state's factory takes the transition time as a required parameter, so no transition can leave it unstamped. For a working state (`in_progress`, `revision_in_progress`, `triage_in_progress`) it answers "running since", to within container start-up. Issues that existed before it was recorded start from their detection time, an upper bound until their next transition. Served on `IssueSummary.EnteredStateAt`.

### Code step

A [Lifecycle step](#lifecycle-step) Foundry's own code runs for the issue, with no worker involved: the current entry of `IssueSummary.Lifecycle` with actor Foundry code and phase running, retrying or waiting. The current entry is the one source for what an issue is doing now; no separate field repeats it. The dashboard's step chip reads it as **Publishing…** (Publish running, read from the run's publish phase), **Retrying publish…** (Publish retrying), **Rebasing…** (Rebase, `rebase_pending`), **Checks running…** (Checks, `checks_pending`), **Merging…** (Merge, `merge_pending`), or **Waiting to catch up** (Merge waiting: `merge_pending` while its [Catch-up Queue](#catch-up-queue) entry waits, read from the issue's catch-up queue entry). A run starting is not a code step but shows on the same chip: any step in phase preparing reads **Preparing…**. A code step the dashboard has no copy for reads as its step token in sentence case. Queue waits, failures, Claude steps and person steps show no chip.

### Lifecycle step

One position on an issue's lifecycle path: Detected, Triage, Implement, Publish, Rebase, Checks, Review, Your review, Merge, Done, and the queue waits before Triage, Implement and Review. Each state type declares its current step (`Issue.LifecycleStep`); a working issue whose run publishes sits on Publish instead. The served `IssueSummary.Lifecycle` gives each step an actor (Claude, Foundry code, person) and a status (done, current, upcoming, off, skipped), and the current step a phase (waiting, preparing, running, needs you, failed, retrying; none once the issue is done). A step is **off** when a repository setting turns it off ([Rebase After Run](#rebase-after-run), [Review Pull Requests](#review-pull-requests), [Merge When Checks Pass](#merge-when-checks-pass)), read live through `IRepositoryLifecycleSettingsQueries`, and **skipped** when it is on but the issue's path does not take it: Merge once [Has Person Feedback](#has-person-feedback) is set or the issue is in Your review, Your review on a merge-on repository otherwise. The server decides the path; the dashboard only renders it. The dashboard's lifecycle rail draws no queue waits: a current wait shows on the node after it, as queued, and a done or upcoming wait is not drawn.

## TriageQueuedIssue

`triage_queued`: an issue waiting for a worker to triage it ([ADR 0097](docs/adr/0097-every-new-issue-is-triaged-before-dispatch.md)). A [Queued Issue](#queued-issue) with `TierRank = 3`, between continuation and fresh. Display group: Queued, label "Triage queued".

- [Run Kind](#run-kind): `Triage`, as for a `detected` issue.
- **Ways in** — every new issue (`DetectedIssue.QueueTriage()`, on creation; an issue still `detected` from before triage existed is sent there by the dependency pass when it has no open blockers), a [TriageBlockedIssue](#triageblockedissue) whose blockers closed, input arriving on an `AwaitingInputIssue` with a `Retriage` [resume target](#awaitinginputissue), Retry triage from `TriageFailedIssue`, and [Re-triage](#re-triage) from `FreshQueued` or `Blocked`.
- Triage waits for open blockers (ADR 0102): when the dependency pass or the claim-time re-check finds open blockers, `Block()` → `TriageBlockedIssue`. Re-triage or an answered question while blockers are open therefore lands here first and then moves to `TriageBlocked`. The dependency pass keeps blockers current in `TriageInProgress`, `TriageFailed` and `AwaitingInput` too, where they are only recorded. Revision, continuation and rebase-conflict runs never go through triage.
- `Context` is `DispatchContext.Triage`, whose role maps to `WorkerRole.Triage`; `DispatchBranchName` is `null`, so no branch name is generated and the run gets none.
- **Ways out** — `Claim()` → `TriageInProgressIssue`; `Block()` → `TriageBlockedIssue`. Hard-deleted on untrack like every queued state.

## TriageBlockedIssue

`triage_blocked`: an issue that hasn't been triaged and has open blockers, or one a triage run found blocked ([ADR 0102](docs/adr/0102-triage-runs-once-blockers-clear.md), [ADR 0103](docs/adr/0103-triage-defers-on-open-dependencies-by-linking-blockers.md)). When its blockers close, it moves to `TriageQueued`. A [Blocked Issue](#blocked-issue) has already been triaged or skipped triage, and moves to `FreshQueued`. Display group: Queued, label "Triage blocked".

- [Run Kind](#run-kind): `Triage`.
- Carries `WorkerRunId`: the triage run whose verdict named the blockers; `null` when the issue waits on blockers found before triage.
- Not a [Queued Issue](#queued-issue): it is never a dispatch candidate and holds no worker slot. Triage waits so the run judges the issue against the code its blockers wrote, not code that doesn't exist yet.
- **Ways in** — the dependency pass applying open blockers to a `DetectedIssue` (`BlockTriage()`) or a `TriageQueuedIssue` (`Block()`), or the claim-time re-check finding them on a `TriageQueuedIssue`. Raises `IssueTriageBlocked`.
  - A `blocked` triage verdict: `TriageInProgressIssue.MarkBlocked(verdict)` (`FromTriageInProgress`) records the [Triage Verdict](#triage-verdict) and sets the blockers to the ones the verdict names, then those already recorded. Raises `IssueTriageBlocked` and `TriageBlockersNamed`, which the Issues bridge turns into the `TriageBlockersFound` integration event; Monitoring links each named blocker on the provider and posts the [Blocker Reason](#blocker-reason) comment.
- **Ways out** — `Unblock()` → `TriageQueuedIssue`, raising `IssueTriageQueued`, when the dependency pass finds its blockers closed or unlinked (as for `BlockedIssue`); `FailBlockerLink(workerRunId, blockerIssueNumber, rejection, failedAt)` → `TriageFailedIssue` with `FailureCategory.BlockerLinkFailed` and `IssueTriageFailed`, when the provider permanently refuses to link a blocker the verdict named (`BlockerLinkFailed`, handled by `BlockerLinkFailedHandler`; ignored, with a log line naming which part did not match, unless the issue is still `TriageBlocked`, the event's run is the state's `WorkerRunId` and the verdict names the blocker). `FailBlockerLink` flattens the provider's text to one line and caps the stored reason at `TriageBlockedIssue.FailureReasonMaxLength`. The verdict and blockers stay on the failed issue. No operator actions. Hard-deleted on untrack.

## TriageInProgressIssue

`triage_in_progress`: an issue a triage worker is reading. Carries `WorkerRunId`. Display group: In flow, label "Triaging". [Run Kind](#run-kind): `Triage`.
Counts against the global slots and the [Repository Worker Limit](#repository-worker-limit) like every running worker.

- **Ways out** — the run's outcome step dispatches by role: a triage run that exits 0 with a schema-valid verdict publishes `TriageRunCompleted(IssueId, WorkerRunId, TriageResult, DecidedAt)` with `TriageResult = Ready(kind, decisions) | NeedsInput(kind, questions, decisions) | Blocked(kind, blockedBy)` instead of `WorkerRunCompleted`, and the merge-request lookup never runs.
  - `Ready` → `MarkReady(verdict, decisions)` records the [Triage Verdict](#triage-verdict), appends a `Decided` [clarification](#clarification) round when the verdict carries decisions, and goes through the dependency check: `FreshQueued` with no open blockers, `Blocked` otherwise.
  - `NeedsInput` → `MarkNeedsInput(verdict, decisions)` records the verdict, opens an `Asked` round carrying its questions and decisions, and enters `AwaitingInputIssue` (`FromTriageInProgress`) with the `Retriage` resume target; its questions are posted on the issue thread.
  - `Blocked` → before any cycle check, `TriageVerdict.Blocked.RejectionOf` rejects a result with no blockers, more than the blocker cap, a blocker named twice, a blank reason or a reason over the reason cap; the handler routes it to `TriageFailedIssue` with `FailureCategory.VerdictInvalid` directly, not via `WorkerRunFailed`. Otherwise the handler asks `DependencyCycleDetector.FindFirstCycleClosedBy` over the stored edges of the repository's other issues whether any named blocker closes a cycle. The detector searches the stored edges for a path from each named blocker back to the issue; a blocker that is the issue itself is a cycle `#n → #n`. None does → `MarkBlocked(verdict)` records the verdict and enters `TriageBlockedIssue` (`FromTriageInProgress`), and the named blockers are linked on the provider (see [TriageBlockedIssue](#triageblockedissue)). One does → the `blocker_cycle` route: `FailOnBlockerCycle(cycle, failedAt)` → `TriageFailedIssue` with `FailureCategory.BlockerCycle`, no link written, and a reason that names the cycle and tells the person to remove one of its links before retrying triage.
  - Any other end — a missing or invalid verdict (`FailureReason.VerdictInvalid`, category `verdict_invalid`: a step failure, never a fallback to facts, [ADR 0094](docs/adr/0094-code-decides-claude-judges-only-what-code-cannot.md)), a non-zero exit, a timeout or a cancellation — goes through `WorkerRunFailed` to `TriageFailedIssue`.
- A `TriageRunCompleted` for an issue no longer in triage with that run (a redelivery, or the issue moved on) is ignored; with the inbox dedup this makes it replay-safe.

## TriageFailedIssue

`triage_failed`: an issue whose triage run failed. Carries `WorkerRunId`, `FailureReason`, `FailureCategory` and `FailedAt`. Display group: Needs you.
Not a `FailedIssue`, whose `Retry()` returns `FreshQueuedIssue` and would skip triage.

- **Causes** — beyond a failed run (non-zero exit, timeout, cancellation, `verdict_invalid`): `blocker_cycle` (`FailureCategory.BlockerCycle`) when a blocker a `blocked` verdict names would close a dependency cycle (from `TriageInProgressIssue.FailOnBlockerCycle`), and `blocker_link_failed` (`FailureCategory.BlockerLinkFailed`) when the provider permanently refused to link a named blocker (from `TriageBlockedIssue.FailBlockerLink`, via `FromTriageBlocked`, which takes the run id from the `BlockerLinkFailed` event). Neither is transient, so both park until Retry triage, Skip triage or an issue edit.
- **Ways out** — `RetryTriage()` → `TriageQueuedIssue` (`POST /api/issues/{id}/retry-triage`); [Skip triage](#skip-triage) (`POST /api/issues/{id}/skip-triage`); a failure whose category is auto-retryable (`transient_api_error`, `provider_unreachable`, `fetch_failed`, `package_source_unreachable`, `package_restore_failed`) retries automatically back to `TriageQueuedIssue` through [`TransientRetryService`](#transient-retry), on that category's own schedule. Any other reason parks it, with one automatic exit: an **issue edit**. Each scheduled poll's failed-triage step (`TriageFailedEditStep`, listing the rows through `ITriageFailedIssueQueries`) reads every `triage_failed` issue's body with `GetIssueStatusAsync` and keeps an [Issue Watch](#issue-watch) for it: the first successful read is the baseline (an edit made before the first pass that sees the failure is part of it), and a body change that outlasts the 2-minute quiet period raises `TriageFailedIssueEdited`, removing the watch in the same save. `TriageFailedIssueEditedHandler` then calls `RetryTriage()` → `TriageQueuedIssue`; it logs and does nothing when the issue is no longer `TriageFailed` (an operator pressed Retry or Skip triage first) or when the edit was first seen before `FailedAt` (`AcceptsEditSeenAt`). A failed body read leaves everything as it is until the next poll; a closed issue is left to the existing close path. An edit that does not fix the cause fails triage again and parks again: one run per edit, never a loop. Hard-deleted on untrack, like the other failed states.

## Triage Verdict

The latest verdict a triage run returned for an issue (`Issue.TriageVerdict`, column `triage_verdict`, `null` until first triaged): `Ready(kind, decidedAt)`, `NeedsInput(kind, questions, decidedAt)` or `Blocked(kind, blockedBy, decidedAt)`, where `blockedBy` is a non-empty list of at most `TriageVerdict.Blocked.MaxBlockers` `TriageBlocker(issueNumber, reason)` naming each open issue once, each with a [Blocker Reason](#blocker-reason) of at most `TriageVerdict.Blocked.ReasonMaxLength` characters. Carried through every transition; a later triage replaces it.
`null` means not yet triaged or a stored verdict that could not be read: an unreadable row loads as no verdict so one bad row cannot make every issue in the repository unreadable. The next state change copies the null into a new row and so drops the unreadable text. A SQL filter on `TriageVerdict == null` does not match such a row before then, because the column itself is not NULL. A verdict is a cached judgement and every consumer tolerates null.
A `ready` or `needs_input` verdict may also carry **decisions** ([ADR 0123](docs/adr/0123-workers-ask-only-about-uncertain-business-logic-and-decide-the-rest.md)): details triage settled itself instead of asking, each a `TriageDecision(question, decision, ground, evidence)` with `ground` one of `settled_business` (an ADR, a domain document, current behaviour or the issue's stated intent settles it) or `implementation_detail`, and 1 to 5 evidence items; at most `TriageResult.MaxDecisions` (20). They are not stored on the verdict: the [clarification log](#clarification) records them. A `blocked` verdict carrying `decisions`, or a decision with blank text, an unknown ground or no evidence, fails the run as `verdict_invalid`.
Its kind decides the issue's [Kind Classification](#kind-classification) only while no label does. Exposed on the issue detail as `triage: {outcome, kind, questions, decidedAt}`, where `outcome` is `ready`, `needs_input` or `blocked` (a `blocked` verdict carries no questions; its blockers and reasons appear as `blockedBy: [{issueNumber, reason}]` on the state details, `reason` being `null` for a blocker a person linked or when the latest verdict names none).

## Triaged Spec Hash

The fingerprint of the spec the issue body stated when the latest triage run was dispatched (`Issue.TriagedSpecHash`, column `triaged_spec_hash`, `null` until a triage run records one, and for every issue triaged before the column existed). It is `IssueBodyHash.OfSpec` of the body `IssueClaimedHandler` read at dispatch: the [Issue Body Hash](#issue-body-hash) normalization, after every task-list marker at a list item's start (`- [x]`, `* [X]`, `+ [ ]`, `1. [x]`, at any indent, a fenced code block included) becomes `[ ]`, so ticking or unticking an acceptance criterion's checkbox is not a rewrite while a reworded criterion, an added one or a typo fix is. The run stores it (`WorkerRun.SuppliedSpecHash`, column `supplied_spec_hash`), `TriageRunCompleted` carries it, and every verdict records it before the issue moves on; a failed triage run records nothing. Carried through every transition with the shared properties. It is the baseline a rewritten spec is compared against, not the body any other run was given, so a revision that ran on a rewritten body never resets it ([ADR 0125](docs/adr/0125-a-rewritten-spec-sends-an-issue-with-open-work-back-to-triage.md)).

## Open Work

The branch and pull request an issue already has while it goes back to triage (`Issue.OpenWork`, columns `open_work_branch_name` and `open_work_pull_request_url`): both values together or none, so a state that holds work requires both to set it. `TriageQueuedIssue.HoldingOpenWork` sets it; it is carried through every transition with the shared properties, through all four triage states and `AwaitingInput` with a `Retriage` target. Leaving triage through the dependency check with Open Work set — a `ready` verdict, Skip triage, or Continue without answer on a re-triage question — resumes it as a [`SpecChanged`](#revision-trigger) revision on its branch and pull request: `RevisionQueued` with no open blockers, `RevisionBlocked` otherwise, never `FreshQueued` or `Blocked`. The branch name is never recomputed from the title or the triage-assigned kind for an issue with Open Work, so a renamed or reclassified issue cannot open a second pull request. The revision state then carries the branch and pull request, and Open Work is cleared. A `needs_input` verdict keeps it on `AwaitingInput`, a `blocked` verdict on `TriageBlocked`. Stored rows from before the columns have none ([ADR 0125](docs/adr/0125-a-rewritten-spec-sends-an-issue-with-open-work-back-to-triage.md)).

## Re-triage

The operator action that sends a `FreshQueuedIssue` or `BlockedIssue` back to `TriageQueuedIssue` (`POST /api/issues/{id}/retriage`; any other state is 409), keeping its recorded blockers. While those blockers are open it then waits as a [TriageBlockedIssue](#triageblockedissue), and is triaged once they close. There is no automatic re-triage once an issue is ready: an edit to a queued or blocked issue does not trigger triage again.

## Skip triage

The operator action that sends a `TriageFailedIssue` through the dependency check without a readiness verdict (`FreshQueued` with no open blockers, `Blocked` otherwise), or an issue holding [Open Work](#open-work) to its `SpecChanged` revision, its kind left as it is: an unclassified issue stays `Unclassified` and gets `feat/`. Any other state is 409. It is fail-open on purpose, for a broken triage role.

## AwaitingInputIssue

`awaiting_input`: an issue whose run stopped to ask a person one or more questions ([ADR 0096](docs/adr/0096-awaiting-input-replies-detected-by-recorded-ids-and-branch-head.md)).
It waits for input on the thread where the question was posted. Display group: Needs you, label "Awaiting input".

- **Way in** — an implement run that exits 0 with a schema-valid `needs_input` verdict resolves to `WorkerOutcome.NeedsInput` (a merged pull request still completes the issue and the verdict is ignored; a non-zero exit still goes through failure classification), and `WorkerRunCompleted` carries the questions. `InProgressIssue.MarkAwaitingInput` and `RevisionInProgressIssue.MarkAwaitingInput` open a [clarification](#clarification) round.
- **Resume target** — recorded at entry from what the asking run left: `Revision(branch, pr)` for an open pull request (always for a revision run), `Continuation(branch)` for commits without a pull request, `Fresh` when nothing was pushed, or `Retriage` when a triage run asked (see [TriageInProgressIssue](#triageinprogressissue)) (column `resume_target`).
- **Question post** — the closed union `Pending | Posted(commentId, postedAt, commentUrl) | Failed(reason, nextAttemptAt, attempts)` (column `question_post`). The state is committed first; the awaiting-input step of each poll (and its `AwaitingInput` [nudge](#nudge)) then posts the questions on the pull request when there is one, otherwise on the issue, and records the comment in the [posted-comment registry](#posted-comment-registry) and starts the issue's [Issue Watch](#issue-watch) in the same save that marks it `Posted`. `QuestionPost.DueAt` is when the step posts: at once while pending, at the retry time after a transient failure, never once posted or after a permanent failure. A transient failure retries with exponential backoff (1 minute doubling, capped at 1 hour); a permanent one is not retried automatically. **Retry post** (`POST /api/issues/{id}/retry-post`) sends any `Failed` post, permanent or waiting on its backoff, back to `Pending` and nudges the step; any other state, or a post that has not failed, is 409.
- **Reminder** — `QuestionReminder`, the closed union `Unsent | Sent(commentId, sentAt) | Failed(reason, nextAttemptAt, attempts)` (column `question_reminder`). Once a `Posted` question has waited the awaiting-input reminder delay (Global Settings, `awaiting_input_reminder_delay_hours`, default 72, positive) since its post time with no input at all (none arrived and none inside its quiet period), the step posts one reminder in the same thread and records it in the [posted-comment registry](#posted-comment-registry) in the same save, so it is never read as a reply. One reminder per round; a failed reminder retries with the question post's backoff and never blocks reply detection. Input after the reminder resumes the issue as usual.
- **Ways out** — [input arrived](#input-arrived) resumes along the resume target: `RevisionQueued` with `RevisionTrigger.InputProvided`, `ContinuationQueued` on the branch with an empty failure reason, or `FreshQueued`, or `TriageQueued` for `Retriage` (the triage prompt renders the clarification log). As in the review pass, a closed provider issue completes it and a pull request closed without merging fails it continuably (`PrClosed`).
- **Continue without answer** — the operator action (`POST /api/issues/{id}/continue-without-answer`; any other state is 409) that closes the open round with a `ContinuedWithoutAnswer` part, whatever the post status, and resumes along the resume target; nothing more is posted. The queued state it enters carries `ContinuedWithoutAnswer` (column `continued_without_answer`) onto its `DispatchContext`, and the run it starts records `RefusesNeedsInput` (column `refuses_needs_input`): that run's `needs_input` verdict is refused, the fact-based outcome applies (open pull request to review, nothing pushed to unchanged, and so on), the publisher opens the pull request as for a finished run, and the refusal stays on the run (`NeedsInputRefused`, from the flag and the recorded verdict). Only that run is restricted: a later run on the issue may ask again. On a `Retriage` target it goes through the dependency check as [Skip triage](#skip-triage) does instead, since a triage run that can't get its answer would only ask again.

## Clarification

One round on an issue's append-only clarification log (`Issue.Clarifications`, column `clarifications`), the closed union `Asked | Decided | Requested` ([ADR 0123](docs/adr/0123-workers-ask-only-about-uncertain-business-logic-and-decide-the-rest.md), [ADR 0126](docs/adr/0126-a-persons-pull-request-feedback-is-recorded-on-the-clarification-log-and-supersedes-the-issue.md)). An `Asked` round holds the questions a run asked, the input that arrived, and when each happened (`askedAt`, `answeredAt`, both or neither), plus the triage decisions made beside the questions (possibly none). A `Decided` round holds only the decisions of a `ready` triage verdict (at least one) and `decidedAt`; it has no input and is never open, so `Issue.OpenClarification` never returns one. A `Requested` round records a person's pull request feedback Foundry consumed (`Issue.RecordPersonFeedback`, from `Review`, `ChecksPending` or `MergePending` into `RevisionQueued`): the comments (body, optional path, optional line), the omitted-comment count and `receivedAt` (the newest consumed comment's time, else the transition time); it asks nothing and is never open. Stored rounds carry `"kind":"asked"`, `"kind":"decided"` or `"kind":"requested"`; a round stored before kinds existed has none and reads as `Asked` with no decisions. A decision is stored as `{question, decision, ground, evidence}` with the schema's ground token.
The input is typed parts (`ClarificationInput`, a non-empty list of the closed union `InputPart`): `Comments(text)` for the settled comments, and `DescriptionEdited` when the issue description changed (issue-thread waits only), in that order, or `ContinuedWithoutAnswer` alone when an operator chose [Continue without answer](#awaitinginputissue). They are stored as a JSON array of `{"kind":"comments","text":...}`, `{"kind":"descriptionEdited"}` and `{"kind":"continuedWithoutAnswer"}`; a round stored before parts existed holds a plain string and reads as one `Comments` part. The prompt renders a round's parts into one text, a description edit as `The issue description was edited after the question was posted.`, and a continue as `No answer arrived; the operator chose to continue. Use your judgment.`
A round is open until input arrives; a round left when the issue moved on without input (closed, or its pull request closed) stays unanswered.
Every implement prompt (fresh, continuation, revision) and every triage prompt renders the log in its own section, so an answer lasts as long as the issue does; with an empty log the prompt is unchanged. Each decision renders inside its round as `<decision ground="…"><question>…</question><text>…</text><evidence>…</evidence></decision>`, XML-encoded and never cut by size fitting, and the section states that a decision stands unless a later comment on the issue overrides it. A `Requested` round renders as `<clarification received-at="…"><review-comments>…</review-comments></clarification>`, each comment after its `path:line`, with the omitted count on its own line. The section states once that a round with person input (an answered `Asked` or a `Requested` round) supersedes the issue text and earlier rounds where they conflict, the newest winning; size fitting cuts older rounds' input first and the newest person-input round last.

## Recommendation

The single option a run proposes for one of its questions, with its reason. A run asks a structured question (`InputQuestion.Structured`, the shared verdict definition's required `recommendation` object): 2 to 4 options, which Foundry letters `a` to `d` by position, and a recommendation naming one of those letters (`RecommendedOption`) with a one-line reason (`RecommendationReason`). A letter beyond the options makes the verdict invalid: the schema cannot compare two fields, so `VerdictQuestionsReader` checks it. A reply that leaves the question unaddressed accepts the recommendation, so a bare "ok" accepts every recommendation, and a reply such as `1b` picks option b of question 1.
Code never applies it: the posted question comment marks the recommended option ✅ inside the lettered option list, with its reason on the same line, and the clarification log renders it as `<recommendation option="…">` after the `<option letter="…">` elements, with the rule stated, so the next run reads the reply and the options together.
A question stored before questions were structured is an `InputQuestion.Legacy` (stored JSON without `$type`): free-text context and recommendation, the recommendation null only for questions stored before runs returned recommendations. Legacy questions keep the old rendering: the comment shows the recommendation as **Recommended:** and keeps the plain reply instruction when none has one, and the clarification log renders `<context>` and `<recommendation>`.

## Input Arrived

After the question's post time, any of:
- a new eligible comment on the question's thread whose ID Foundry did not post: not a bot, not a system note, not in a resolved thread, not in the [posted-comment registry](#posted-comment-registry), whoever the author is (the PAT owner's own reply counts);
- while the question lives on the issue thread, an issue body whose [Issue Body Hash](#issue-body-hash) differs from the [Issue Watch](#issue-watch)'s baseline. On a pull request thread the body is not watched: ticking acceptance-criteria checkboxes during review edits it.
- while the resume target has a branch (a revision or a continuation), a [Branch Head](#branch-head) that differs from the Issue Watch's baseline: someone answered by acting, for example by committing the denied change themselves. A deleted branch is not input (for a revision the closed pull request way out applies); a branch recreated after a baseline `Absent` is.

Each input settles after the 2-minute quiet period (`ActionableFeedbackPolicy.QuietPeriod`): a comment from its `created_at`, a body or head change from the time Foundry first saw it. The Issue Watch alone decides the round, under one rule: nothing arrives while any input is still inside the quiet period, and one `AwaitingInput` nudge is scheduled for when the last of it settles; then all of it arrives together in one `AwaitingInputArrived`, at the newest input's time — a `Comments` part (several comments joined, oldest first), then a `DescriptionEdited` part, then a `BranchUpdated(fromSha, toSha)` part from the baseline head to the settled one (`fromSha` null when the branch did not exist when the question was posted). So a follow-up comment is never dropped because an earlier one settled first, a burst of pushes is one round from the baseline to the latest head, and a comment plus an edit or a push resume the issue once. The issue thread is read with the [issue-thread reader](#issue-thread-reader), a pull request thread with `GetReviewFeedbackAsync`, both with the post time as the cutoff; the body comes with the step's closed-state read (`GetIssueStatusAsync`), so it costs no extra request. A revision's head is the head of the pull request status the step already reads, also at no extra request; a continuation's costs one `GetBranchHeadAsync` read per pass.

## Issue Watch

Monitoring's per-issue record of the provider state Foundry compares against while it waits on a person (`IssueWatch`, table `issue_watches`, keyed by repository and issue number). It holds the baseline taken when the question was posted and, for each kind, a pending change with the time Foundry first saw it. The kinds are the issue body: the baseline [Issue Body Hash](#issue-body-hash) (empty until a body read succeeds, and on pull-request-thread waits) and a pending body hash with its first-seen time; and the resume branch's head: the baseline [Branch Head](#branch-head) (empty until a head read succeeds, and when the resume target has no branch) and a pending head SHA with its first-seen time.
A read that differs from the baseline becomes pending; a further different read replaces it and restarts its time; a read equal to the baseline clears it, so an edit reverted, or a branch force-pushed back to its baseline, within the quiet period is never input. A head read `Absent` against a `Present` baseline (the branch was deleted) records no change and clears a pending one. A pending change settles once it has stayed the same for the 2-minute quiet period. A body or head read that fails changes nothing; the next pass reads again. A wait posted before watches existed gets one on its first pass, and a watch saved before heads were watched has no head baseline: in both, the first successful read becomes the baseline.
The record is replaced when a question is posted and removed when the issue stops waiting: in the same save that dispatches `AwaitingInputArrived`, or by the next full awaiting-input pass for any other exit.
A [TriageFailedIssue](#triagefailedissue) is watched too, for its body alone (no question was asked, so no comment counts): the failed-triage step starts the watch with the body read on the first pass that sees the failure, and removes it in the same save that dispatches `TriageFailedIssueEdited`, or on its next pass once the issue left `triage_failed` by any other exit. Each watch records the wait it belongs to (`Wait`: `AwaitingInput` or `TriageFailed`, column `wait`); each step removes only its own wait's watches and replaces a watch left from the other wait, so neither drops a change the other is still settling.

### Branch Head

Where a waiting issue's resume branch points, as one provider read reports it (`BranchHead`): `Present(sha)` or `Absent` (the provider answered 404). GitHub reads `GET repos/{owner}/{repo}/git/ref/heads/{branch}` (`object.sha`), GitLab `GET projects/:id/repository/branches/:branch` (`commit.id`); any other non-success response is a failed read. A revision's head comes from its pull request status instead; a status without a head is a failed read.

### Issue Body Hash

The lowercase hex SHA-256 of an issue body's UTF-8 bytes after CRLF and CR become LF and trailing whitespace is trimmed (`IssueBodyHash.Of`), so bodies that differ only in line endings or trailing whitespace are the same body. A missing description is the empty string. `IssueBodyHash.OfSpec` also normalizes every task-list marker to `[ ]` first; it fingerprints the [Triaged Spec Hash](#triaged-spec-hash).

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
Carries `MergeWhenChecksPass` (non-nullable `bool`, default `false`) — see [Merge When Checks Pass](#merge-when-checks-pass).
Carries the [Package Sources](#package-source) its root `nuget.config` declares, as last read by the poll.

**Removed Repository** ([ADR 0117](docs/adr/0117-removed-repositories-are-soft-deleted-and-restored-on-re-add.md)): removing a repository (`RepositoryRemoval`, the single removal path) deletes its webhooks, sets `RemovedAt`, raises `MonitoredRepositoryRemovedDomainEvent`, deletes its repository-owned rows (`IssueWatch`, `PendingMerge`, `PendingRebase`, `PostedComment`) and takes it out of the [Repository Priority](#repository-priority) order; its row is kept. The named query filter `RemovedRepositoryFilter` hides a removed repository from every query except the two that bypass it by name: the History slug lookup (`RepositorySlugQueries`), so completed issues keep showing its slug, and the re-add lookup. Re-adding the same (Host, Repository Slug) restores it with the add request's settings; its `Id`, webhook hook id and webhook secret are kept, and its operational state is reset to what a new repository starts with (eligibility `Unreachable`, write probe `Unknown`, webhook status `NotRegistered` with no Webhook Spec Stamp or failed registration, no `LastPolledAt` or untrack suppression, no root `nuget.config` state, active, placed at the end of the order).
Removal reaches the other modules as `MonitoredRepositoryRemoved` (bridged from `MonitoredRepositoryRemovedDomainEvent` into the outbox in the removal's transaction). Issues deletes every issue of the repository except `completed` ones, which stay in History; an issue whose worker is still running (`triage_in_progress`, `in_progress`, `revision_in_progress`, `review_in_progress`) and whose run is not waiting for a publish retry is kept and logged; one whose run is a `PublishFailedRun` holds no live process and is deleted. Workers discards every unpublished run of the repository that is discardable (`PublishFailedRun`, retrying or exhausted, and `PublishBlockedRun`) with `FailureReason.RepositoryRemoved` and deletes its Hand-off volume; a `PublishingRun` is left to finish, and every other run is kept for per-repository statistics. Both handlers raise no per-issue events and are no-ops on redelivery. Re-adding the repository restores its History, and its open issues return from the provider on the next poll.
Removal is refused (`RepositoryErrors.RemovalBlocked`, `409 Conflict` naming each blocking issue as `<slug>#<number> (<state>)` and listing them in the `blockingIssues` extension) while any issue of the repository is in `triage_in_progress`, `in_progress`, `revision_in_progress` or `review_in_progress`, unless its run is waiting for a publish retry (`PublishFailedRun`, retrying or exhausted: no container and no Publisher is running, and the run is discarded with the repository); an in-flight issue whose run is not yet created blocks. `RepositoryRemoval` checks once before deleting the hooks, so a refused removal never deletes them, and again, authoritatively, inside its write transaction. A claim re-checks inside its own write transaction that the candidate's repository is not removed (`IRepositoryRemovalQueries.IsRemovedAsync`) and otherwise skips with `ClaimSkipped`; SQLite serializes the two transactions, so either the claim commits and the removal is refused, or the removal commits and the claim is skipped. The later writer that SQLite refuses (`SQLITE_BUSY`) fails without writing: the removal returns an error, the claim's delivery is retried by the outbox. A run is bounded by the worker timeout, so a refusal always clears on its own.

**Unclaimed Repository**: a Monitored Repository whose owner namespace no account's [Namespace Claim](#namespace-claim) covers on its host. It cannot be polled, dispatched or published until an account claims the namespace or the repository is removed. Membership is resolved live from claims (`ICredentialResolver`), never from stored eligibility. `GET /api/repositories/unclaimed` lists them (`UnclaimedRepositorySummary`: id, host, slug, the `no-credential:<namespace>` violation and its description), ordered by host then slug, in their own section of the repository settings page; removed repositories are excluded. `DELETE /api/repositories/{id}` removes any repository through `RepositoryRemoval` without an account scope (`204`, `404` for an unknown or removed id, `409` while work is in flight); for an Unclaimed Repository no account resolves, so its hook deletion is skipped and logged. An Unclaimed Repository leaves that state when an account claims its namespace (account creation, token rotation, repository recheck or a claim recheck) or when it is removed. While any Unclaimed Repository exists on a host, the claims of every account on that host are re-derived hourly (`UnclaimedRepositoryReclaimService`) and on demand (`POST /api/repositories/unclaimed/recheck?host=<host>`, returning `reclaimedRepositoryCount`, the host's repositories unclaimed before and covered after); `ClaimRecheck` processes the accounts in `Id` order through token rotation's never-steal claim subtraction, so when two accounts can reach a newly unclaimed namespace the first claims it. An account whose namespaces cannot be derived is skipped and logged; a host with no Unclaimed Repository makes no provider call.

### Apply Branch Rules

The persisted operator intent on a Monitored Repository (`ApplyBranchRules`, non-nullable, default `true` on the add request; existing rows migrated to `false`) that Foundry may write its default-branch rules.
Acted on only on explicit operator action (`BranchRuleEnforcer`): when the repository is added, when a new account token is saved through the account update (every opted-in repository the Account covers that is `Ineligible` solely through `branch-protection:*` violations, processed sequentially; one failure never stops the rest or fails the account update; an update that leaves the token untouched writes nothing), or through the repository card's "Apply branch rules" action (`POST /api/accounts/{accountId}/repositories/{id}/apply-branch-rules`), which also flips the persisted intent to `true` — an explicit opt-in, even for a repo opted out at add time. That action is offered only when the repository is `Ineligible` solely through `branch-protection:*` violations, and only for a repository the account serves (same host, covered by its Namespace Claims). Polling stays read-only toward the provider and never writes or repairs rules.

### Rebase After Run

The per-repository operator toggle (`RebaseAfterRun`, non-nullable, default `true` for new repositories; existing rows migrated to `true`) that turns the post-run rebase on or off (see [Rebase Pending Issue](#rebase-pending-issue), [ADR 0087](docs/adr/0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md)).
Set on the add request (`rebaseAfterRun`, default `true`) and toggled from the repository form through the update request (`rebaseAfterRun`; omitted leaves it unchanged); surfaced on the repository summary.
Read by the Monitoring module's rebase step when it runs — never snapshotted, so a change mid-run wins. When off, a rebase-pending issue resolves straight away with `RebaseOutcome.Disabled`.
Unlike [Apply Branch Rules](#apply-branch-rules), existing rows default to `true`: the rebase writes only Foundry-owned branches, never repository-wide protection.

### Merge When Checks Pass

**Merge When Checks Pass** — per-repository opt-in, non-nullable `bool`, default `false`. When on, Foundry merges the pull request it opened itself once its checks gate resolves `Passed`, pinned to the head SHA the gate verified. Read by the Monitoring module's merge step when it runs, never snapshotted. Not the provider's native auto-merge, which Foundry never arms ([ADR 0105](docs/adr/0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)).
Set on the add request (`mergeWhenChecksPass`, default `false`) and toggled from the repository form through the update request (`mergeWhenChecksPass`; omitted leaves it unchanged); surfaced on the repository summary and as a "Merges on pass" chip on the repository list. Existing rows migrate to `false`.
The live value is also copied onto `ChecksVerdictResolved.AutoMerge` when the poller records a verdict, so switching it on while an issue is in `checks_pending` merges that issue. Switched off between the verdict and the merge step, the issue resolves `MergeOutcome.Disabled` and goes to review unmerged.

### Review Pull Requests

**Review Pull Requests** — per-repository opt-out, non-nullable `bool` (`MonitoredRepository.ReviewPullRequests`, column `review_pull_requests`), default `true`; existing rows migrate to `true`. Review is the safer path, so an operator switches it off rather than on. When on, a checks gate that resolves `Passed` or `NoneDetected` on a known head sends the issue to [`review_queued`](#review-queued-issue) instead of `merge_pending` or `review`: the review role judges the pull request first ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). Read when the poller records a verdict and copied onto `ChecksVerdictResolved.ReviewPullRequests`, next to `AutoMerge`, never snapshotted: switching it off while an issue is already queued for review lets that review run, and the next gate resolution reads the new value. Set through the repository API (`reviewPullRequests` on create and update; an update that omits it keeps the stored value) and the repository form's "Review pull requests" toggle; a repository with it off shows the "No review" setting tag.

### Has Person Feedback

`Issue.HasPersonFeedback` — non-nullable `bool`, default `false` (column `has_person_feedback`; existing rows migrate to `false`). Set once a person's review feedback drives a revision (`RevisionTrigger.ReviewFeedback` from `review`, `checks_pending` or `merge_pending`) and carried through every transition with the shared properties, so nothing in the issue's lifetime clears it — a retry on a new pull request included, which fails safe to review. While it is true, a `Passed` verdict with Merge When Checks Pass on routes to `review`, never to `merge_pending`: the person sees the fix before it lands. Other revision triggers (`ReviewFindings`, `FailedChecks`, `RebaseConflict`, `InputProvided`) never set it, so auto-merge survives a [findings fix](#review-fix-attempts) ([ADR 0105](docs/adr/0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md) § "Person in the loop").
The merge step reads the pull request's review feedback since `FeedbackCutoffAt` before it merges. Actionable comments resolve `MergeResolved` with outcome `feedback`, which revises the issue (`MergePendingIssue.Revise`) and never merges; comments still inside the quiet period postpone the merge to a not-before nudge at `HeldBackUntil` without counting a transient failure. A failed read counts against the merge's transient budget.

### Branch Rules Outcome

The per-request result of an apply attempt, returned in the add response (`CreateRepositoryResponse.branchRules`) and the apply-branch-rules response (`ApplyBranchRulesResponse.branchRules`, where `not-requested` cannot occur) and never persisted: `applied`, `already-present`, `not-requested`, `skipped` (`access-failure`, `unverifiable`) or `failed` (`insufficient-permission`, `provider-error`).
The write is attempted only when the full evaluation is `Ineligible` solely through `branch-protection:*` violations; it uses the account token, which needs GitHub **Administration: read and write** or the GitLab **Maintainer** role ([ADR 0115](docs/adr/0115-one-account-token-verified-against-every-permission-foundry-uses.md)). A token without that permission is already `Denied` by the write probe, so its repository is never solely `branch-protection:*`-ineligible; a token the provider still refuses is `failed` (`insufficient-permission`). The rules are then re-read (no second write probe) and the stored eligibility reflects that re-read, never the write's status code — a write that succeeds but does not take effect is `failed` (`provider-error`).
A failed or skipped apply never fails the add. On the account-token save path the per-repository outcomes are not returned; the account update's affected repositories report each repository's eligibility after enforcement.
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
New and restored repositories append at the end (highest existing Position + 1); removing a repository renumbers survivors contiguously, and a removed repository holds no position in the order.
Polling is independent of position.

## Repository Worker Limit

The maximum number of workers that may run concurrently for a single monitored repository.
Modeled as `MaxConcurrentWorkers` — a non-nullable `int` on `MonitoredRepository`, constrained to the range 1..20, defaulting to 1 (see [ADR 0074](docs/adr/0074-per-repository-capacity-in-candidate-selection.md)).
Enforced in `DispatchCandidateSelector` (Issues module) — not at the Workers-side dispatch gate — because the gate receives an anonymous capacity token and cannot see which repository will spend it; see ADR 0074 for the architectural rationale.
The in-flight count against this limit is `count(InProgressIssue) + count(RevisionInProgressIssue) + count(TriageInProgressIssue)` minus the issues whose run is an `UnpublishedRun` (publishing, retrying, exhausted or blocked; `IWorkerRunQueries.GetIssueIdsWithUnpublishedRunAsync`), grouped by repository, computed Issues-side as its own query (`InFlightWorkerCountQuery`). A run whose `claude` container stopped holds no [Worker Slot](#worker-slot), so its issue no longer counts while it publishes; an issue just claimed, whose run does not exist yet, still counts.
Issues in review (`ReviewIssue`) do not count; fresh, revision and triage workers all count, so the limit means "workers", not "implement runs".
This count is independent of the global `MaxConcurrent` gate and of the Workers-side [Worker Slot Occupancy](#worker-slot-occupancy) (which counts reservations, starting runs and active runs but carries no repository identity); both leave out a run once its `claude` container stops.
Lowering the limit below the current in-flight count does not preempt running workers — the repository drains to the new limit as its workers finish.

## Dispatch Order

The total order that governs which queued issue is claimed next and the order the dashboard list query serves queued issues in.
The comparison primitive is the four-tuple `DispatchOrderKey` — `(TierRank, Position, IssueNumber, Id)`, evaluated in that precedence:

- **TierRank** — a property of the queued state variant that encodes dispatch-priority class: revision (0) < review (1) < continuation (2) < triage (3) < fresh (4) ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md), replacing ADR 0097's order). A review sits behind revisions so it never overtakes a revision answering a person, and ahead of every other run because it only judges work already done. Lower rank is claimed first; this is the primary ordering criterion.
- **Position** — the `EligibleRepository.Position` of the repository that owns the issue, supplied externally into the key. Lower position is preferred within a tier.
- **IssueNumber** — the provider's issue number (the per-project `iid` on GitLab), lowest first within the same repository and tier, reflecting author intent rather than when Foundry noticed the issue (see [ADR 0025](docs/adr/0025-shared-dispatch-order.md)). `DetectedAt` is not a key component.
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
Five variants:

- **`Fresh`** — a new implementation attempt; carries `BranchName`.
- **`Revision`** — a revision cycle; carries `BranchName`, `PullRequestUrl`, and a polymorphic [Revision Trigger](#revision-trigger) describing why the revision was queued.
- **`Continuation`** — resuming an existing branch after a failed run; carries `BranchName` and optional `FailureReason`.
- **`Triage`** — a triage run ([ADR 0097](docs/adr/0097-every-new-issue-is-triaged-before-dispatch.md)); carries nothing, and its `Role` is `DispatchRole.Triage`. The run gets no branch.
- **`Review`** — a review run ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)); carries `BranchName`, `PullRequestUrl`, `ReviewedCommit` (the [Verified Head](#verified-head) the reviewer judges), `CarriedPatchId` and `ChecksPassed` (whether the checks gate recorded `Passed`, false when absent; see [Review Facts](#review-facts)), and its `Role` is `DispatchRole.Review`, which Workers maps to the `review` worker role. The run pushes nothing, so it gets no branch of its own (`DispatchBranchName` is null).

`DispatchContext` is assembled by the concrete `QueuedIssue` subtype overriding the abstract `Context` property — `FreshQueuedIssue` returns `Fresh`, `RevisionQueuedIssue` returns `Revision`, `ContinuationQueuedIssue` returns `Continuation`, `TriageQueuedIssue` returns `Triage`, `ReviewQueuedIssue` returns `Review`.
The handler and claimer treat `Context` as an opaque value — they forward it into `ClaimedIssueDispatch` without switching on it.
Survives the outbox round-trip via `[JsonPolymorphic]` / `[JsonDerivedType]` annotations.

## Run Kind

The kind of worker run an issue is running, or will run when it's next dispatched (#1059). It is declared by the issue state (`Issue.RunKind`, `null` for states with no run ahead) and shown as the board's lead tag on Pipeline and Blocked rows and on worker cards. Values are `Triage`, `Fresh`, `Continuation`, `ReviewFix`, `ChecksFix`, `ConflictFix`, `InputResume`, `Review` and `FindingsFix`; `ReviewFix` to `InputResume` and `FindingsFix` split a `Revision` by its [Revision Trigger](#revision-trigger) (`ReviewFeedback`, `FailedChecks`, `RebaseConflict`, `InputProvided` and `SpecChanged`, `ReviewFindings`). `FindingsFix` (the reviewer's findings) stays distinct from `ReviewFix` (a person's feedback). Run Kind is related to [Dispatch Context](#dispatch-context) but is not the same thing. Dispatch Context is the payload handed to the worker and exists only on queued states. Run Kind is a flat display fact that also covers in-flight and blocked states. It always implies the [Worker Role](#worker-role): `Triage` → triage, `Review` → review, everything else → implement.

- `detected`, `triage_queued`, `triage_blocked` → `Triage`.
- `queued`, `blocked` → `Fresh`.
- `continuation_queued`, `continuation_blocked` → `Continuation`.
- `revision_queued`, `revision_blocked` → `ReviewFix`, `ChecksFix`, `ConflictFix`, `InputResume` or `FindingsFix`, by its trigger.
- `triage_in_progress` → `Triage`.
- `in_progress` → `Fresh` or `Continuation`, by its origin (see [In-Progress Issue](#in-progress-issue), #1060).
- `revision_in_progress` → `ReviewFix`, `ChecksFix`, `ConflictFix`, `InputResume` or `FindingsFix`, by its trigger.
- `review_queued`, `review_in_progress` → `Review` ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)).
- Every other state → `null`.

Served on `IssueSummary.RunKind` as `triage | fresh | continuation | review_fix | checks_fix | conflict_fix | input_resume | review | findings_fix`, or null. The board labels them `triage`, `implement`, `continue`, `review fix`, `checks fix`, `conflict` and `resume`; screen readers hear "Run: <label>". A Pipeline, Blocked or worker issue whose run kind is null or unknown to the dashboard is left unplaced rather than tagged with a guess. The live announcement for a worker that starts reads "#<n> started: <label>".

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
Six variants (`ReviewFeedback`, `ReviewFindings`, `FailedChecks`, `RebaseConflict`, `InputProvided`, `SpecChanged`):

- **`ReviewFeedback`** — a human reviewer requested changes; carries `Comments`, `OmittedCommentCount`, and `NewestConsumedCommentAt`.
- **`ReviewFindings`** (`$type` `review_findings`) — Foundry's review run requested changes ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)); carries a snapshot of the verdict: `Findings` (blocking first, then advisory, each in verdict order, with path, line, category and rule), `UnmetCriteria` (id, the criterion's text and the reviewer's evidence), `TestsAssessment` and `TestsReason`. The snapshot comes from `ReviewRunCompleted`, so the prompt needs nothing from the review run. The prompt renders blocking findings as `path:line — body (category: …; rule: …)`, the unmet criteria with their evidence, the tests assessment, and advisory findings under a "Consider" heading; over the prompt budget it keeps blocking findings, then unmet criteria, then the tests assessment, then advisory findings, cuts the first entry that misses and notes how many were dropped. It never sets [Has Person Feedback](#has-person-feedback), and it spends one [Review Fix Attempt](#review-fix-attempts). It consumes no comments, so the next review cutoff keeps the reviewed state's.
- **`FailedChecks`** — the PR's provider checks went red within the fix budget; carries the `FailingChecks`.
- **`RebaseConflict`** (`$type` `rebase_conflict`) — the post-run rebase conflicted with the default branch within the conflict budget (see [Rebase Conflict Attempts](#rebase-conflict-attempts)); payload-free. The worker rebases the branch onto the default branch with `git rebase origin/HEAD`, resolves every conflict at each stop (preserving both the branch's intent and the default branch's changes), continues with `git rebase --continue` and runs the tests; it discovers the conflicting paths from `git rebase` itself and never merges ([ADR 0101](docs/adr/0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md)). The [Publisher](#publisher) pushes the rebased tip with a lease. A rebase that resolves every conflict by taking the default branch drops every run commit, so the publish is `NothingToPublish`; a run that rebases cleanly leaves the branch zero commits behind, so the next rebase step records `UpToDate`.
- **`InputProvided`** (`$type` `input_provided`) — a person answered the questions an earlier run asked (see [AwaitingInputIssue](#awaitinginputissue)); payload-free. The answer lives on the issue's [clarification log](#clarification), which every implement prompt renders. It consumes no review comments, so the next review cutoff anchors to the run's start time and the answer comment is not read again as feedback.
- **`SpecChanged`** (`$type` `spec_changed`) — the issue description was rewritten after the issue had [Open Work](#open-work), and triage judged the rewritten issue ready; payload-free. The prompt tells the worker the issue description was rewritten since the last triage and to bring the branch in line with the issue as it now reads. Like `InputProvided` it consumes no review comments, so it carries no `PriorFeedbackCutoffAt` and the next review cutoff anchors to the run's start time. It starts a new round (`Issue.Round`) and resets the review and checks fix budgets; its [Run Kind](#run-kind) is `InputResume`, as a person's rewrite is their input ([ADR 0125](docs/adr/0125-a-rewritten-spec-sends-an-issue-with-open-work-back-to-triage.md)).

Modelling the trigger as a closed union makes a half-populated revision (comments *and* failing checks, or neither) unrepresentable, and lets the `SystemPromptBuilder` switch on the kind rather than null-checking fields.
Persisted as a single `revision_trigger` JSON column and survives the outbox round-trip via `[JsonPolymorphic]` / `[JsonDerivedType]` annotations.

## Checks Fix Attempts

The number of `FailedChecks` revisions dispatched for the code currently pushed — `Issue.ChecksFixAttempts`, column `checks_fix_attempts`, default 0. It lives on the `Issue` base and is carried through every transition with the shared properties, like [Review Fix Attempts](#review-fix-attempts), so no state can drop it by forgetting a copy.
`ChecksPendingIssue.FixChecks` increments it. New code resets it to 0: any `ReviewFeedback` revision (`ReviewIssue.Revise` / `ChecksPendingIssue.Revise` / `MergePendingIssue.Revise`), a findings fix, an answered question resuming a revision, an implement run that opened a pull request, a catch-up rebase, and the pull request head moving while in `review` (`ChecksPendingIssue.FromReview`). Two transitions set it to the cap instead, because no new code follows: a checks rerun on the same head (`ChecksPendingIssue.FromReviewAfterChecksRerun`, [ADR 0108](docs/adr/0108-a-review-issue-whose-checks-pass-on-a-rerun-returns-to-the-checks-gate.md)) and a `FailedChecks` revision run that published no commit (`RevisionInProgressIssue.MarkUnchangedRebasePending`).
Only a fixable failing check starts a fix run: one whose script ran and failed. A failing check that never ran its script is an **infrastructure failure** (`FailingCheck.IsInfrastructureFailure`), and a fix run has nothing to fix in it.
The providers classify each failing check in `Checks` (Monitoring): on GitLab a failed job is fixable only when its `failure_reason` is `script_failure`, `job_execution_timeout`, `job_execution_server_timeout` or `server_timeout_running`, or when it carries no reason; every other reason (e.g. `ci_quota_exceeded`, `runner_system_failure`, `unknown_failure`, or one GitLab adds later) is an infrastructure failure, and the reason is the check's conclusion. On GitHub a check run concluding `FAILURE` or `TIMED_OUT`, or a status context in `FAILURE`, is fixable; a check run concluding `STARTUP_FAILURE`, `CANCELLED` or `ACTION_REQUIRED`, or a status context in `ERROR`, is an infrastructure failure. Every failing check still makes the verdict red.
When a red verdict arrives that names at least one fixable failing check and the count is below the cap (`ChecksMaxFixPushes`, = 2), the issue re-queues as a `FailedChecks` revision naming only the fixable checks.
At the cap it transitions to `ReviewIssue` with `ChecksOutcome.StillFailing`.
A red verdict naming no failing check also transitions to `ReviewIssue` with `ChecksOutcome.StillFailing`, and spends no attempt.
A red verdict whose failing checks are all infrastructure failures (at least one, every one with `IsInfrastructureFailure`; `Checks.Red.IsInfrastructureFailure`) resolves to `ChecksOutcome.InfrastructureFailed` (discriminator `infrastructure_failed`) carrying every failing check: CI couldn't run, so the issue goes to `ReviewIssue` without a fix run and without spending an attempt, and the card and pane ask the person to retry the failed jobs on the provider. Like any non-`Passed` verdict, a green retry on the same head is picked up by the review pass's re-read ([Verified Head](#verified-head)). A mixed verdict (script and infrastructure failures) stays `StillFailing`.

### Owed Checks Retry

A `FailedChecks` revision run that published no commit found the failure outside the branch (a flaky test, a runner fault), so it owes the red head one retry of its failing CI jobs, which needs no Claude call. The red verdict's head rides on the trigger (`RevisionTrigger.FailedChecks.HeadSha`); `RevisionInProgressIssue.MarkUnchangedRebasePending` records it as `OwedChecksRetryHeadSha` on `RebasePendingIssue`, which carries it into `ChecksPendingIssue` (column `owed_checks_retry_head_sha`) unless the rebase moved the head (`RebaseOutcome.Rebased`). Leaving `checks_pending` drops it.
When the checks pass reads a red (`still_failing`) verdict on exactly that head, it resolves each failing check's job (`IIssueProvider.ResolveCiJobAsync`) and retries it (`IIssueProvider.RetryCiJobAsync`: GitLab `POST /projects/:id/jobs/:job_id/retry`, GitHub `POST /repos/{owner}/{repo}/actions/jobs/{job_id}/rerun`). It retries only when every failing check is a CI job that ran its script; when all retries succeed it records `ChecksRetried` instead of a verdict, and `ChecksPendingIssue.RecordChecksRetried` keeps the issue in `checks_pending` with the retry spent, its gate clock (`EnteredChecksPendingAt`) restarted and `ChecksRetriedAt` set. Otherwise (a check that is no CI job, an infrastructure failure, a non-2xx retry answer) the verdict resolves as before and goes to review, the fix budget being exhausted.
While `ChecksRetriedAt` is set, a red read is not counted: it may still be the verdict the retry answered. A pending read records `ChecksRetryRestarted`, which clears it; a red read at the gate's upper bound resolves `still_failing` all the same.

## Review Fix Attempts

The number of `ReviewFindings` revisions (findings fixes) the current round of review has started — `Issue.ReviewFixAttempts`, column `review_fix_attempts`, default 0 ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). It lives on the `Issue` base and is carried through every transition with the shared properties, like [Has Person Feedback](#has-person-feedback), so no state can drop it by forgetting a copy.
A `ChangesRequested` verdict below the cap (`Issue.ReviewMaxFixRuns`, = 2) queues a findings fix and increments it; at the cap the verdict goes to `review`, so a reviewer and an implementer cannot loop. A failed findings-fix run leaves it unchanged, and an approval after a findings fix keeps it.
A person-triggered round resets it to 0: a `ReviewFeedback` revision, or the pull request head moving while in `review` ([ADR 0106](docs/adr/0106-a-checks-verdict-is-bound-to-the-head-commit-it-was-read-against.md)). A checks rerun on the same head (`ReviewIssue.RecheckAfterChecksPassed`) is not a person's round and keeps it. A [Carried Approval](#carried-approval) is an approval and keeps it.

## Latest Review

The issue's most recent review verdict — `Issue.LatestReview` (Issues `Domain/ValueObjects/LatestReview.cs`), column `latest_review` (JSON `{"run_id","outcome","patch_id"}`), null until a review completes ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). It holds the review run's id, its `ReviewVerdictOutcome` and the zero-context patch-id of the diff it judged (null for a diff with no changes). `ReviewInProgressIssue.ResolveVerdict` records it from `ReviewRunCompleted`; it lives on the `Issue` base and is carried through every transition with the shared properties, like [Review Fix Attempts](#review-fix-attempts).
Its **carriable patch-id** (`LatestReview.CarriablePatchId`) is the patch-id of an `Approved` or `ApprovedForPerson` verdict, and null for `ChangesRequested`. `ReviewQueuedIssue` passes it to dispatch as `DispatchContext.Review.CarriedPatchId`.

## Review Facts

The facts code computes for a review run at dispatch (`SuppliedReviewFacts`, Workers, [ADR 0114](docs/adr/0114-a-restricted-review-run-reads-a-code-prepared-packet-at-a-read-only-commit.md)): the pull request and reviewed commit, the diff's patch-id, every changed path with its class, the lines each hunk adds, the issue's acceptance criteria (`AC1`…), and **checks passed** (`ChecksPassed`, [ADR 0119](docs/adr/0119-review-criteria-can-be-covered-by-the-checks-gate-and-the-reviewer-reads-the-pull-request-description.md)). Checks passed is `true` when the checks gate recorded `Passed` on the reviewed commit and `false` for `NoneDetected`; `ReviewQueuedIssue` sets it on `DispatchContext.Review.ChecksPassed`. The run records the facts (JSON column `supplied_review_facts`), its prompt shows them, with a `### Checks gate` section that offers `covered_by_checks` only when checks passed, and its verdict is checked against them after the run. Facts recorded before checks passed was supplied read it as `false`.

### Pull Request Description (review prompt input)

Next to the review facts, a review run's prompt shows the pull request's **description**, read live from the provider at dispatch through `IIssueContentQueries.GetPullRequestDescriptionAsync` (GitHub's `body`, GitLab's `description`; [ADR 0119](docs/adr/0119-review-criteria-can-be-covered-by-the-checks-gate-and-the-reviewer-reads-the-pull-request-description.md)), so a person's edits are included. It is read only after the [Carried Approval](#carried-approval) check, so a carried review makes no extra provider call. The `### Pull request description` section holds it XML-encoded in a `<pull-request-description>` block, capped at 20,000 characters (`MaxPullRequestDescriptionChars`) behind an omission note, with the instruction to use it only for criteria about the description: its claims about the change are not evidence. An empty description reads "The pull request has no description." A failed read logs a warning, shows "The pull request description could not be read.", and the review proceeds, so such a criterion is answered `not_verifiable`. Unlike the review facts, the description is not recorded on the run, and no check after the run reads it.

## Review Criterion Status

A review verdict's answer for one acceptance criterion (`ReviewCriterionStatus`, verdict token in brackets):

- **`Met`** (`met`) — the diff shows the change satisfies the criterion.
- **`Unmet`** (`unmet`) — the change does not satisfy it; code requests changes.
- **`NotVerifiable`** (`not_verifiable`) — the diff and checkout cannot show either; code leaves the approval to a person (`ApprovedForPerson`).
- **`CoveredByChecks`** (`covered_by_checks`) — the criterion's whole claim is that a build, test or CI run passes, and the checks gate passed ([ADR 0119](docs/adr/0119-review-criteria-can-be-covered-by-the-checks-gate-and-the-reviewer-reads-the-pull-request-description.md)). It counts as met when code derives the outcome. When the [Review Facts](#review-facts) say checks did not pass, code rewrites the answer to `NotVerifiable` with evidence saying `covered_by_checks` was unavailable, before deriving the outcome, rather than rejecting the verdict. The rewritten answer is the one recorded and shown. The PR comment lists it as "covered by checks" and keeps it out of "Criteria not met", and the dashboard's Review pane shows it with the chip "Covered by checks".

## Carried Approval

A review completed without a run because the change is unchanged since the issue's latest approval ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)): typically a clean catch-up rebase. When the Publisher's prepared patch-id equals `DispatchContext.Review.CarriedPatchId` and the issue's latest recorded review run is that approval, `IssueClaimedHandler` starts no container. The run completes with the earlier verdict bound to the new head and marked `CarriedFrom` (`ReviewCarry`: the run that judged the change and the commit it judged; a carry of a carry keeps naming the original review), and raises `ReviewRunCompleted` with the earlier outcome and the new commit, so the issue routes as the approval was routed. Its PR comment is one line: "Approval carried to `sha7`: the change is unchanged since the review of `sha7`." A different patch-id (a `RebaseConflict` revision or a person's push rewrote the diff), no patch-id, or an earlier `ChangesRequested` verdict runs the review in full. CI still runs on the new head before the review is queued.

## Rebase Conflict Attempts

The number of consecutive rebase conflicts that dispatched a `RebaseConflict` revision, carried on `RebasePendingIssue` and threaded through `RevisionQueuedIssue`, `RevisionInProgressIssue`, `RevisionFailedIssue`, `ChecksPendingIssue` and `ReviewIssue`, so it survives `RebasePending → RevisionQueued → RevisionInProgress → RebasePending → ChecksPending → Review` (column `rebase_conflict_attempts`).
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

## Merge Outcome

**Merge Outcome** — how Foundry's own merge of the pull request resolved when the issue left `merge_pending` ([ADR 0105](docs/adr/0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)). A closed union (`MergeOutcome`, Issues `Domain/ValueObjects/`), modelled on [Rebase Outcome](#rebase-outcome):

- **`Merged`** — the provider merged the pull request (or someone else merged it first); the issue completes.
- **`Disabled`** — [Merge When Checks Pass](#merge-when-checks-pass) was off when the merge step ran.
- **`Skipped(reason)`** — the merge did not happen: `not_mergeable` (the provider refused: conflicts, a branch rule, missing approvals, or no merge implementation), `unauthorized` (the token may not merge), `head_moved` (new commits reached the pull request after the verdict, so the pinned merge was refused, or before a [catch-up](#catch-up) started), `provider_error` (transient failures exhausted the retry budget), `fell_behind` (the merge was refused as behind again after every catch-up round its [catch-up queue](#catch-up-queue) entry allows), `catch_up_failed` (a catch-up rebase resolved `Skipped`, `Disabled`, or `Conflict` at the [conflict cap](#rebase-conflict-attempts); the review issue keeps that rebase's own `RebaseOutcome`) or `draft` (a person converted the pull request back to a draft after Foundry [marked it ready](#marked-ready)).

A merge refused because the pull request is behind the default branch no longer resolves an outcome when [Rebase After Run](#rebase-after-run) is on: it joins the [catch-up queue](#catch-up-queue) instead ([ADR 0111](docs/adr/0111-foundry-serializes-catch-up-rebases-for-pull-requests-it-merges.md)).

`Disabled` and `Skipped` are persisted on `ReviewIssue` (`merge_outcome` JSON column, null for a review issue that never entered `merge_pending`) and exposed on the issue DTOs as `mergeOutcome` plus `mergeSkippedReason`. The dashboard reads a skipped merge as the **Merge skipped** reason in Needs you and badges it on the issue card; `Disabled` reads as an ordinary "PR ready".

## Review Outcome

**Review Outcome** — why the review step handed a pull request to a person without the reviewer's verdict ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). A closed union (`ReviewOutcome`, Issues `Domain/ValueObjects/`), modelled on [Merge Outcome](#merge-outcome), with one variant:

- **`Skipped(reason)`** — the review run failed with no automatic retry left; `reason` is the run's failure category token (`verdict_invalid`, `model_unavailable`, `transient_api_error` once its retries are spent, …).

Persisted on `ReviewIssue` (`review_outcome` JSON column, `{"discriminator":"skipped","reason":"…"}`), null for a review issue whose review step did not fail open or that never went through it. Served on the issue summary and state details as `reviewOutcome` and `reviewSkippedReason`. A person can retry a skipped review (**Retry review**, `POST /api/issues/{id}/retry-review`): `ReviewIssue.RetryReview()` → `ReviewQueuedIssue` for the same reviewed commit (the Verified Head) with the `AutoMerge` the skipped review carried, which `ReviewIssue` keeps in `review_auto_merge` (false for an issue that did not come from the review step); any other review issue answers 409. Not the Workers-side `ReviewVerdictOutcome` (`Approved`, `ApprovedForPerson`, `ChangesRequested`), which code derives from a valid verdict and which routes the issue instead.

## Claim

The act of assigning a queued issue to a specific worker run — the transition from a queued state variant to an in-progress one, paired with the `IssueClaimed` integration event that tells the Workers module to start a container.

A claim is authorized by exactly one `WorkerCapacityAvailable` event, which carries only a `WorkerRunId`.
Each event is backed by a durable [Dispatch Reservation](#dispatch-reservation) created atomically with the event; the reservation counts against slot occupancy from the moment it is written, preventing additional events from being published until the slot is consumed or released.
Before selecting a candidate, the handler checks whether any issue already carries the event's `WorkerRunId` — if so, the claim is skipped without selecting, preventing double-claims on redelivery (the at-least-once outbox guarantee means the same event may arrive more than once after a crash between claim and inbox commit).
An event that finds no candidate publishes `Claim Skipped`, which releases the reservation.

Claiming proceeds in three steps:

1. **Resolve eligible repositories** — every repository owning a queued issue is checked against Repository Eligibility, and eligible ones contribute their `Position`. Resolving all repositories up front (rather than checking the head candidate) means an ineligible repository cannot block dispatch of issues behind it.
2. **Select the winner** — the claimable issues across all eligible repositories and all tiers are sorted by Dispatch Order key; the head of the sorted list whose repository has resolvable dispatch info wins. If the best candidate's repository cannot yield dispatch info, that candidate is skipped and the next-best is tried — the tick is NOT aborted. Dispatch info resolution is memoized per repository within a tick, so a second candidate from the same repository reuses the cached result without a second query.
3. **Claim it** — call `Claim(workerRunId)` on the aggregate, publish `IssueClaimed` carrying a `ClaimedIssueDispatch` (assembled from the winning candidate's pre-fetched dispatch info and the aggregate's own `DispatchBranchName` and `Context`), and transition the aggregate. The event and the state change commit as one atomic unit.

Claiming is the only path from a queued state to an in-progress one; nothing else in the system claims an issue. Claims are never concurrent — the outbox relay is a single host-level service that delivers sequentially.

**Terminal outcomes** are logged once at the handler: `Debug` when no eligible repositories exist or no candidates remain after eligibility filtering, `Warning` when every candidate was skipped because its repository's dispatch info could not be resolved (`AllCandidatesUnresolvable`). Ineligibility filters out repositories with no account in step 1 once their stored eligibility records the lost claim: the poll records a repository no account covers as Ineligible (`no-credential:<namespace>`) on its next tick, and deleting an account removes the repositories it covers. This path is therefore reached only in the narrow race between a claim being lost (or a token being cleared) and the next poll tick.

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

The `NeverProbed` and `RateLimited` causes recover via the `Unknown` self-heal cadence — the repository is automatically re-probed on the next poll cycle once a 15-minute cooldown has elapsed since the last attempt. `BranchRulesUnavailable` is retried on the next poll cycle unconditionally. A `Denied` verdict is re-probed automatically once a one-hour cooldown has elapsed since `Denied.ProbedAt` (a permission gained in place on the provider — a fine-grained token edited, a GitLab role raised — raises no credential event; see [ADR 0115](docs/adr/0115-one-account-token-verified-against-every-permission-foundry-uses.md)). `Granted` verdicts remain event-triggered, except that any verdict checked against an older permission list is re-probed on the next poll (see the permission-list version below).
Stored on the Monitored Repository and composed from two checks of different cadence:

- **Branch-rules GET (per-cycle)** — re-evaluated unconditionally on every poll cycle and synchronously at repository creation. A configuration change on the provider is reflected on the next poll without user action (auto-heal). A failed GET produces `Unreachable(BranchRulesUnavailable)`.
- **Write probe (event-triggered for Granted; self-healing for Unknown and Denied)** — runs on repository add, manual re-check, and credential update/rotation. The last result is persisted on `MonitoredRepository` as a `WriteProbeVerdict` value object (`Granted` / `Denied` / `Unknown`), which carries an `UnknownReason` (`Transport` or `RateLimited`). The verdict is composed with the fresh branch-rules result each cycle. `Unknown(Transport)` maps to `Unreachable(NeverProbed)`; `Unknown(RateLimited)` maps to `Unreachable(RateLimited)`. Both are automatically re-probed after a 15-minute cooldown. `Denied` is automatically re-probed after a one-hour cooldown: each probe stamps `Denied.ProbedAt`, and a `Denied` row persisted before that field existed counts as due. `Granted` is only re-probed by operator or credential events, or when its permission-list version is outdated, so steady-state polling sends no write probes. Manual re-check and credential update probe at once whatever the cooldown. A failed probe stamps `Unknown.LastAttemptedAt` so the next automatic retry is one cooldown away. See [ADR 0054](docs/adr/0054-split-eligibility-cadence.md).

**GitHub 403 rate-limit classification.** A GitHub write-probe 403 is not always a missing-permission denial.
Classification keys on explicit rate-limit response headers: a 403 with `X-RateLimit-Remaining: 0` or a present `Retry-After` header is treated as rate-limit exhaustion and produces `Result.Fail(RateLimitExhausted)` → `Unknown(RateLimited)`.
A 403 with headroom or no rate-limit headers stays `Missing` → `Denied` → `CannotPush` (fail-closed for genuine permission denial).

**Permission list.** One list per provider (`TokenPermissions`, [ADR 0115](docs/adr/0115-one-account-token-verified-against-every-permission-foundry-uses.md)) names everything the account token needs, and how each entry is verified. The account form's requirements, the GitHub creation link, the probes and the GitLab minimum role all derive from it.

| Provider | Permission | Verified by |
|---|---|---|
| GitHub | Repository access: Only select repositories | not checked — GitHub cannot report a token's repository selection |
| GitHub | Contents, Issues, Pull requests (read and write) | write probe: `POST git/refs`, `issues`, `pulls` — 422 or 404 granted |
| GitHub | Administration (read and write) | write probe: `POST rulesets` with `{}` — 422 granted |
| GitHub | Webhooks (read and write) | write probe: `POST hooks` with `{}` — 422 granted |
| GitHub | Commit statuses (read) | read probe: `GET commits/{default branch}/statuses?per_page=1` — 200 granted |
| GitHub | Actions (read and write) | write probe: `POST actions/jobs/1/rerun` (a job id that does not exist) — 404 granted |
| GitHub | Metadata (read) | always granted |
| GitHub | Workflows (read and write) | not verifiable before use; a push touching `.github/workflows/` without it is refused at publish |
| GitLab | `api` scope | token validation |
| GitLab | Maintainer role (access level 40) | the token user's project or group role on the repository |

Every probe reads a 403 that is not rate-limited as *missing*, and a rate-limited 403 as `Unknown(RateLimited)`. The Administration, Webhooks and read probes read any other status — 404 included, which is what GitHub Enterprise Server without the rulesets API answers — as indeterminate (`Unknown`), never as granted; the Actions probe reads 404 as granted and any other status as indeterminate. The GitHub sequence runs every probe and collects every missing permission, so the operator learns them all in one pass; it stops only at an indeterminate probe. A GitHub token that is not fine-grained is never probed: its repositories are `Denied` with no missing permissions (see [Token Validation Outcome](#token-validation-outcome)). The writable-repository listing used for namespace claims keeps the Developer role threshold; only eligibility requires Maintainer.

**Permission-list version.** Every `WriteProbeVerdict` (`Granted`, `Denied`, `Unknown`) carries the `PermissionListVersion` of the provider's list it was checked against — a hash of the list's verified entries (key and verification kind), so adding or changing a verified entry changes it with no hand edit. A verdict whose version is not current, including a verdict persisted before versions existed, is due for a write probe on the next poll whatever its kind or cooldown, and the poller re-runs the full evaluation for it. `Denied` also carries `MissingPermissions`: the stable permission-list keys of what the token lacks (a legacy `Denied` row deserializes with none), from which the `cannot-push` description is derived.

A manual "re-check" action forces immediate re-evaluation of both checks.
Only `Eligible` repositories have their queued issues dispatched — ineligibility gates dispatch only; detection, dependency reconciliation, and review polling continue regardless. Already-running workers are unaffected.
Issue-level ineligibility is derived from the repository for display, never stored per issue.

## Eligibility Violation

A value object describing a specific, user-actionable eligibility precondition failure on a repository — an access failure (`no-credential:<namespace>`, `cannot-push:<slug>`), a Branch Protection failure (`branch-protection:*`), or a package source failure: `package-source-unreachable:<url>` for each declared [Package Source](#package-source) whose [probe verdict](#package-source-probe-verdict) for the repository's credential is not `Readable` (`Unchecked` included, so a repository never dispatches against an unprobed source), and `nuget-config-invalid` when its root `nuget.config` is recorded as invalid.
An account failure (`no-credential`, `credential-unreadable`, `credential-invalid`) short-circuits everything else. Package source violations join `cannot-push` or Branch Protection violations in one `Ineligible` list, and replace an `Eligible` or `Unreachable` result: they name a cause the operator can fix, and every one of these states blocks dispatch. Once the verdict turns `Readable`, or the source is no longer declared, the next evaluation clears the violation. Applying branch rules judges the rules without package source violations.
Carries `Rule` (a well-known string constant, e.g. `"branch-protection:allow-direct-pushes"`), persisted as the sole state.
`Description` (human-readable explanation for dashboard display) is **derived** from `Rule` (and the repository's provider type, for access-related rules such as `cannot-push`), and for `cannot-push` the missing permissions on the repository's `Denied` verdict: "Token is missing {A}, {B} and {C} for {slug}." on GitHub, "Token's user needs the Maintainer role on {slug}." on GitLab, and the generic text when the verdict names none, at read time in `RepositoryMappings.ToEligibilityInfo` — it is not persisted.
Legacy rows that stored a `Description` field deserialize without throwing (the field is accepted but ignored); the derived value is always used for display.
Stored as a non-empty collection on the `Ineligible` variant of Repository Eligibility, surfaced on the repository card in settings.
Provider-unreachable is modeled as the separate `Unreachable` eligibility variant rather than a violation, keeping the violation list strictly actionable.

## Issue Dependency

A directed "blocked by" relationship between two issues within the same repository.
Stored as a collection of blocking issue numbers on the Issue aggregate — the blocking issue may or may not be tracked by Foundry.
Both GitHub (REST API v2026-03-10) and GitLab (Issue Links API, Premium+) expose dependencies via structured APIs.
Foundry fetches dependencies during the detection poll cycle and reconciles them on each subsequent poll.
Because the poll can lag behind the provider (GitLab sends no webhook when an issue link is created), and a new issue is queued for triage on creation before its first dependency pass, Foundry also re-reads the open blockers of every `FreshQueuedIssue`, `TriageQueuedIssue`, `RevisionQueuedIssue` and `ContinuationQueuedIssue` live from the provider immediately before claiming it, so an issue with an open blocker starts no run regardless of webhook delivery ([ADR 0124](docs/adr/0124-open-blockers-hold-every-implement-run.md)).
A claim-time hit is applied exactly as the poll path applies detected dependencies — the issue transitions to its blocked state (`BlockedIssue`, `TriageBlockedIssue`, `RevisionBlockedIssue` or `ContinuationBlockedIssue`) and the next candidate in dispatch order is tried within the same capacity event; when every candidate is blocked or unreadable, the claim is skipped and the capacity reservation is released.
A failed claim-time read skips the candidate for that dispatch without changing its state; the next dispatch tick retries it.
Revisions are re-checked whatever their [Revision Trigger](#revision-trigger), and continuations whatever queued them (Retry, an answered question, a branch adopted at claim time), so every path into those queues meets the gate. A `ReviewQueuedIssue` is not re-checked, and a blocker never interrupts a run in progress or the code-owned steps (rebase, checks, review, merge): the dependency pass only records it there, and it applies when the issue next enters `RevisionQueued` or `ContinuationQueued`.
When the provider does not expose dependency links (e.g. GitLab Free tier), the provider degrades gracefully — it treats the issue as having no dependencies and logs once, rather than failing the poll.
Circular dependencies are detected by a domain service and flagged via a `CircularDependencyDetected` domain event.
A cycle does not exempt an issue from blocking: a `FreshQueuedIssue` in a cycle still transitions to `BlockedIssue`, an untriaged one to `TriageBlockedIssue`, a queued revision or continuation to `RevisionBlockedIssue` or `ContinuationBlockedIssue`, and the event is still raised. A running or failed triage, or an issue awaiting input, only records its blockers; they decide where a ready verdict sends it.
The cycle is resolved by breaking it in the provider, after which the dependency pass unblocks the issue.
A blocker is *resolved* when it is closed in the provider — the provider client filters out closed blockers at the anti-corruption boundary so domain logic never sees them.
The provider client also scopes "blocked by" links to the same repository or project at this boundary — GitHub by `repository.full_name`, GitLab by the linked issue's numeric `project_id` — so cross-project and cross-repository links never reach domain logic.
Link entries whose project or repository cannot be determined are kept as blockers (fail-safe toward blocking).
When a blocker's state is missing or unrecognized, it is treated as still blocking (fail-safe).

**Claim-time branch check** — beside the blocker re-check, a `FreshQueuedIssue` that has no open blockers has its branch read from the provider (a compare against the default branch) immediately before it is claimed. Foundry chooses between Fresh and Continuation from its own records, which can be lost while the issue's branch and pull request survive on the provider (a database reset, or a repository removed and re-added); a Fresh run over such a branch would re-implement the issue and diverge from it at publish.
- The branch has at least one commit the default branch lacks: `FreshQueuedIssue.AdoptBranch()` requeues it as a `ContinuationQueuedIssue` on that branch with no failure reason, raising `IssueContinuationQueued`. It leaves the Fresh queue like a blocked candidate, the next candidate in dispatch order is tried, and a later dispatch pass claims it as a Continuation.
- The branch does not exist (the provider answers NotFound) or has no commits ahead: the issue is claimed Fresh.
- Any other read failure skips the candidate for that dispatch without changing its state; it never falls back to Fresh, and the next dispatch tick retries it.
- `TriageQueuedIssue` candidates are not branch-checked. Retry Fresh deletes the branch before requeueing, so its run stays Fresh.

## Blocked Issue

A lifecycle state for a triaged issue that has unresolved dependencies (an untriaged one waits as a [TriageBlockedIssue](#triageblockedissue)); a queued revision or continuation with open blockers waits as a [Revision Blocked Issue](#revision-blocked-issue) or a [Continuation Blocked Issue](#continuation-blocked-issue).
A triaged issue (a ready verdict or Skip triage) with open blockers transitions to `BlockedIssue` instead of `FreshQueuedIssue`. Its [Run Kind](#run-kind) is `Fresh`.
A `FreshQueuedIssue` that gains blockers is demoted to `BlockedIssue` — whether the blockers are found by the dependency pass or by the claim-time re-check (see Issue Dependency).
A `FreshQueuedIssue` whose blockers form a cycle is blocked like any other; `CircularDependencyDetected` is raised alongside the transition.
When all blockers are resolved — that is, closed in the provider, or the link removed (including one that broke a cycle) — a `BlockedIssue` transitions to `FreshQueuedIssue` on the next dependency pass, without being triaged again. [Re-triage](#re-triage) sends it back to triage, where it waits as a `TriageBlockedIssue` while its blockers are open and is triaged once they close.

## Blocker Reason

The one-sentence reason a triage run gives for each open issue it names in a `blocked` verdict (`TriageBlocker.Reason`, never blank, at most `TriageVerdict.Blocked.ReasonMaxLength` characters).
It is shown in the posted comment and next to the blocker on the dashboard.

## Rebase Pending Issue

A lifecycle state (`rebase_pending`, Active / In flow) for an issue whose worker run left an open pull request: Foundry is rebasing the worker branch onto the default branch through the provider before the checks gate ([ADR 0087](docs/adr/0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md)). The agent never force-pushes: when this rebase conflicts, a `RebaseConflict` revision rebases the branch itself and the [Publisher](#publisher) pushes the rebased tip with a lease ([ADR 0101](docs/adr/0101-worker-branches-catch-up-by-rebase-and-foundry-pushes-with-a-lease.md)).
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, `EnteredRebasePendingAt`, `FeedbackCutoffAt`, [`RebaseConflictAttempts`](#rebase-conflict-attempts) and its [Rebase Pending Reason](#rebase-pending-reason) — all non-nullable — plus `CatchUpHeadSha`, set only for a catch-up (the head the refused merge was pinned to; shares the `merge_head_sha` column).
Entered from `InProgressIssue.MarkRebasePending` (a run that completed with `MergeState.Open`) and from `RevisionInProgressIssue.MarkRebasePending` / `MarkUnchangedRebasePending` (a revision that reported a branch and PR, or pushed nothing — the branch may still be behind), all with reason `AfterRun`; and from `MergePendingIssue.CatchUp` with reason `CatchUp` (the head of the [catch-up queue](#catch-up-queue) starts a [catch-up](#catch-up)). A merged PR or no PR keeps the existing `Completed` / `Unchanged` transitions, with no rebase.
Entering raises `IssueEnteredRebasePending`, which nudges Monitoring's rebase step; the scheduled poll is its backstop. The step's progress (pre-rebase head, request time, transient-failure backoff) is persisted per episode in `pending_rebases`, so a host restart resumes it.
The step: if `RebaseAfterRun` is off → `Disabled`; otherwise it reads the PR head and compares it with the base (`behind_by = 0` → `UpToDate`, no rebase call); otherwise it asks the provider to rebase, guarded by the head it read, and the issue stays `rebase_pending` until the PR head differs from that pre-rebase head (`Rebased`), or (GitLab) the provider reports the rebase finished, or 5 minutes pass (`Skipped(timeout)`). A stale head (a push landed in between) re-reads the head and retries, up to 3 times (`Skipped(stale-head)`). Transient provider failures (5xx, timeouts, rate limits) back off exponentially (30 s doubling, capped at 10 min) driven by nudges and the backstop poll; after 5 the outcome is `Skipped(provider-error)`.
Transitions: `ResolveRebase(RebaseOutcome)` → `RevisionQueuedIssue` with a `RebaseConflict` trigger (a `Conflict` below the [conflict cap](#rebase-conflict-attempts); raises `IssueRevisionQueued`) or else `MarkChecksPending(RebaseOutcome)` → `ChecksPendingIssue` (raises `IssueChecksPending`, so checks nudges start only after the rebase landed; reason `AfterRun`, or `CatchUp` for a catch-up). A `CatchUp` rebase moves on to the checks gate only once its branch is up to date (`Rebased` or `UpToDate`); any other outcome that does not queue a conflict revision (`Skipped`, `Disabled`, or `Conflict` at the cap) goes to `ReviewIssue` via `ReviewIssue.FromFailedCatchUp`, with `ChecksOutcome.Passed`, `MergeOutcome.Skipped(catch_up_failed)`, the rebase's own `RebaseOutcome` and `CatchUpHeadSha` as the Verified Head (raises `IssueInReview`); an `AfterRun` conflict at the cap still fails open to the gate. `Complete()` → `CompletedIssue` (issue closed) and `Fail()` → `ContinuableFailedIssue` (PR closed without merge) — the same exits `ChecksPendingIssue` takes. A checks nudge never applies a verdict to a rebase-pending issue.

### Rebase Pending Reason

**Rebase Pending Reason** — why an issue is in `rebase_pending`: `AfterRun` (discriminator `after_run`: a worker run or revision left an open PR) or `CatchUp` (discriminator `catch_up`: the head of the [catch-up queue](#catch-up-queue) is catching up). A closed union on `RebasePendingIssue` (`rebase_pending_reason` JSON column; rows that predate it migrate as `AfterRun`), modelled on [Checks Pending Reason](#checks-pending-reason). Unlike that reason it changes routing: a `CatchUp` rebase that cannot bring the branch up to date goes to review as `Skipped(catch_up_failed)`.

## Catch-up Queue

**Catch-up queue** — the per-repository, `QueuedAt`-ordered set of merge-pending pull requests whose merge was refused because they are behind the default branch ([ADR 0111](docs/adr/0111-foundry-serializes-catch-up-rebases-for-pull-requests-it-merges.md)). Owned by `MergeQueue` (Monitoring `Domain/Entities/`), built each pass from the repository's `pending_merges` rows; each queued row carries `QueuedAt` (when the entry first joined; ties order by issue number), [`CatchUpRounds`](#catch-up), `AwaitsCatchUp` and `WaitingForIssueNumber` (the issue a waiting head waits for, set while an up-to-date pull request holds it and cleared otherwise).

- **Joining** — the pinned merge returned `NotMergeable`, [Rebase After Run](#rebase-after-run) is on, and `IIssueProvider.IsAncestorAsync(slug, "HEAD", branch)` reads `false` (the branch lacks the default-branch head; `HEAD` resolves to it on both providers). The row is queued (`QueuedAt` set to now unless already set) and waits; no `MergeResolved` is raised and the issue stays `merge_pending`. An ancestry read of `true`, or a non-transient failure, resolves `Skipped(not_mergeable)`; a transient failure counts against the row's transient budget. With Rebase After Run off there is no ancestry read.
- **Waiting** — a waiting entry attempts no merge. Each pass still runs the disabled check, the closed and merged detection and the feedback read, so actionable feedback resolves `feedback` as for any merge-pending issue.
- **Head** — the queued row with the earliest `QueuedAt` among issues still holding a place. Only the head starts a catch-up; it waits while another issue in `checks_pending` or `merge_pending` (not catching up, not itself queued, no outcome resolved, [Has Person Feedback](#has-person-feedback) false) reads up to date (`IsAncestorAsync` `true`); a failed read does not hold it. Otherwise, when the pull request's current head still equals the merge-pending `HeadSha`, it resolves `MergeResolved` outcome `catch_up`; a moved head resolves `Skipped(head_moved)`.
- **Keeping a place** — a row survives while its issue is in `rebase_pending` or `checks_pending` with reason `CatchUp`, or back in `merge_pending`. Re-entering `merge_pending` after a catch-up restarts the episode (transient count and backoff) but keeps `QueuedAt` and `CatchUpRounds`, and the entry attempts its merge once more; refused as behind again, it waits again.
- **Releasing a place** — any other resolution (merged, sent to `review`, feedback, closed) takes the row out of the queue, and an unfiltered pass removes the rows of issues in none of the holding states (queued for any revision, closed, untracked). A later merge-pending episode starts a fresh entry at the back with zero rounds.
- **Re-evaluation** — a pass that merges a pull request nudges the merge step for the queue head; joining the queue as the head nudges it too. The scheduled poll is the backstop for every other change (an awaited issue leaving the checks gate, a person merging to the default branch).
- **Display** — the issue detail reads an entry through `IMergeQueueQueries` (Monitoring contract): its rounds out of `MaxCatchUpRounds`, and, for a waiting entry, its 1-based **position** among the queued rows of held issues in head order (`QueuedAt`, then issue number; 1 means next to catch up) and the awaited issue. When a pass changes what an entry shows (a waiting entry joins, moves up, or changes its awaited issue) without a state change, the merge step delivers the ephemeral `MergeQueueChanged` directly through `IIntegrationEventProcessor`, and Issues broadcasts `IssueUpdated` for each named issue so an open pane reloads its detail. The issue summary also reads the waiting state, as the Merge [Code step](#code-step) in phase waiting: the summary paths read the waiting entries of every repository with a merge-pending issue in one batch (`IMergeQueueQueries.GetWaitingIssuesAsync`), and the same `IssueUpdated` broadcast carries the new step to the board card.

## Catch-up

**Catch-up** — one round of rebase, checks and merge for the [catch-up queue](#catch-up-queue) head: `merge_pending` → `rebase_pending` (reason `CatchUp`) → `checks_pending` (reason `CatchUp`) → `merge_pending`, through the existing rebase step and checks gate. `CatchUpRounds` counts them per queue entry, capped at 3 (`MergeQueue.MaxCatchUpRounds`): an entry that has used every round and is refused as behind again resolves `Skipped(fell_behind)`. A conflicting catch-up rebase below the [conflict cap](#rebase-conflict-attempts) queues the usual `RebaseConflict` revision, which releases the entry's place.

## Merge Pending Issue

**Merge Pending Issue** — a lifecycle state (`merge_pending`, Active / In flow, [Code step](#code-step) Merge, running, or waiting while its catch-up queue entry waits, stage Merge, never Needs action) for an issue whose checks gate resolved `Passed` on a repository with [Merge When Checks Pass](#merge-when-checks-pass) on: Foundry is merging the pull request it opened ([ADR 0105](docs/adr/0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)). The state, step and event mirror [Rebase Pending Issue](#rebase-pending-issue).
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, `HeadSha` (the [Verified Head](#verified-head) the merge is pinned to), `ChecksOutcome`, `RebaseOutcome`, `RebaseConflictAttempts`, `FeedbackCutoffAt` and `EnteredMergePendingAt`.
Entered only from `ChecksPendingIssue.ResolveVerdict`, the single transition that routes a checks verdict: `Passed` with `AutoMerge` and a known head → `MergePendingIssue`; a red verdict naming [fixable failing checks](#checks-fix-attempts) under the fix cap → a `FailedChecks` revision; everything else → `ReviewIssue`. A `Passed` verdict with no head goes to review, because a merge must be pinned to a commit. With [Review Pull Requests](#review-pull-requests) on, a green verdict goes to [`review_queued`](#review-queued-issue) first, and the merge is entered from `ReviewInProgressIssue.ResolveVerdict` instead (`MergePendingIssue.FromReviewInProgress`): an `Approved` review of a `Passed` pull request with `AutoMerge` on and no person feedback, pinned to the reviewed commit, so a push since the review is refused as `head_moved`.
Entering raises `IssueEnteredMergePending`, which nudges Monitoring's merge step; the scheduled poll is its backstop. Transient-failure progress, and the issue's [catch-up queue](#catch-up-queue) entry, are persisted per episode in `pending_merges`.
The step: if `MergeWhenChecksPass` is off → `Disabled`; if the provider issue is closed → `ProviderIssueClosed`; if the pull request was merged elsewhere → `Merged`, closed without merge → `ProviderPullRequestClosed`; then it reads review feedback ([Actionable Feedback](#actionable-feedback) → `feedback`); an entry waiting in the catch-up queue then tries to start its [catch-up](#catch-up) instead of merging; otherwise it [marks a draft ready](#marked-ready) (a re-drafted, already-marked pull request → `Skipped(draft)`) and calls the provider merge pinned to `HeadSha` (`Merged`; `HeadMoved` → `Skipped(head_moved)`; `NotMergeable` → the queue when the branch is behind and [Rebase After Run](#rebase-after-run) is on, else `Skipped(not_mergeable)`; `Unsupported` → `Skipped(not_mergeable)`; `Unauthorized` → `Skipped(unauthorized)`). Transient failures back off exponentially (30 s doubling, capped at 10 min); after 5 the outcome is `Skipped(provider_error)`. The outcome reaches Issues as `MergeResolved`; a stale one for an issue no longer in `merge_pending` is ignored.
Transitions: `Complete()` → `CompletedIssue` (merged, or provider issue closed); `ToReview(MergeOutcome)` → `ReviewIssue` via `ReviewIssue.FromMergePending`, carrying the checks, rebase and merge outcomes; `CatchUp()` → `RebasePendingIssue` with reason `CatchUp` (`MergeResolved` outcome `catch_up`; raises `IssueRebasePending`, which nudges the rebase step); `Fail()` → `ContinuableFailedIssue` (`PrClosed`, pull request closed without merge). Not a resting state: untracking preserves it.

## Draft pull request

**Draft pull request** — every pull request Foundry opens starts as a draft (on GitLab, a merge request whose title carries the `Draft:` prefix). A draft requests no CODEOWNERS review and cannot be merged. GitHub create sends `draft: true`; GitLab create prefixes the title with `Draft: `. There is no setting ([ADR 0120](docs/adr/0120-foundry-opens-pull-requests-as-drafts-and-marks-them-ready-once-its-review-cycle-ends.md)). A pull request the provider reports as already open for the branch keeps the draft state it has. A person's comment on a draft is actionable feedback, as on any pull request.

## Marked ready

**Marked ready** — Foundry marks the pull request ready for review once, when its issue first reaches `ReviewIssue` or `MergePendingIssue` with the pull request still a draft. It also counts as marked ready when a done-state finds the pull request already non-draft. The issue records the URL of the pull request it marked (`MarkedReadyPullRequestUrl`); `IsPullRequestMarkedReady` is that URL equalling the issue's current pull request URL. Foundry never converts a pull request back to draft, and a person's re-draft is respected.
Reconciled from observed state, never from an event ([ADR 0120](docs/adr/0120-foundry-opens-pull-requests-as-drafts-and-marks-them-ready-once-its-review-cycle-ends.md)): the review pass of the repository poller and the merge step read the pull request's draft state, which they already read. Unmarked and a draft → `IIssueProvider.MarkPullRequestReadyAsync`, then `PullRequestMarkedReady`; unmarked and not a draft → `PullRequestMarkedReady` without a provider call; marked → no call. A failed call in the review pass records nothing, so the next poll retries; in the merge step it is a transient failure counted against the merge's budget. A marked pull request found a draft again in the merge step resolves `Skipped(draft)` to review. The checks gate and the review step leave a draft a draft.

## Review Issue

A lifecycle state for an issue whose worker completed successfully and produced a PR.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, and `FeedbackCutoffAt` — all non-nullable — and a nullable [`MergeOutcome`](#merge-outcome), set when the issue came from `merge_pending` without a merge (`ReviewIssue.FromMergePending`, which also stores the merge's pinned head as the Verified Head).
A failed [catch-up](#catch-up) also enters review (`ReviewIssue.FromFailedCatchUp`) with `Skipped(catch_up_failed)`.
The review step enters it too (`ReviewIssue.FromReviewInProgress`, [ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)): the reviewed commit becomes the Verified Head, so a person's push during the review sends the issue back through the checks gate, and a nullable [`ReviewOutcome`](#review-outcome) records `Skipped(reason)` when the review run failed open.
Awaits human review of the PR. The monitoring service polls the provider for PR/issue status and review feedback.
`FeedbackCutoffAt` filters stale feedback — only review comments created after this timestamp are considered actionable. Tracks consumed feedback: set to the worker run's start time on first entry to review; updated to the newest consumed comment's timestamp on re-entry after a review-feedback revision. A revision that consumed no comments (a `FailedChecks` or `RebaseConflict` trigger) keeps the cutoff of the checks-pending or rebase-pending state it started from, carried as `PriorFeedbackCutoffAt` through `RevisionQueuedIssue`, `RevisionInProgressIssue`, `RevisionFailedIssue`, `PublishBlockedIssue` and `PublishFailedIssue` (column `prior_feedback_cutoff_at`), so a comment posted while the issue waited is still read; without one (an `InputProvided` trigger, or a row that predates the column) it falls back to the run's start time. In steady state the cutoff never uses the worker run's completion time.
Transitions: `Revise()` → `RevisionQueuedIssue` (feedback detected); `RecheckAfterHeadMoved()` → `ChecksPendingIssue` via `ChecksPendingIssue.FromReview` (the [Verified Head](#verified-head) no longer matches the PR's head; fix budget reset to 0; raises `IssueChecksPending`); `RecheckAfterChecksPassed()` → `ChecksPendingIssue` via `ChecksPendingIssue.FromReviewAfterChecksRerun` (the stored `ChecksOutcome` is not `Passed` and the review pass read green checks on an unchanged head, reported as `ProviderPullRequestChecksPassed`; reason `ChecksRerun`; fix budget starts exhausted at `ChecksMaxFixPushes`, so a red second read goes to review without a fix run; raises `IssueChecksPending`; returns null and changes nothing when the outcome is `Passed`, [ADR 0108](docs/adr/0108-a-review-issue-whose-checks-pass-on-a-rerun-returns-to-the-checks-gate.md)); `Complete()` → `CompletedIssue` (issue closed); `Fail()` → `ContinuableFailedIssue` (PR closed without merge — branch exists).

### Verified Head

**Verified Head** — the head commit of a pull request, as read by the PR status read in the poll that resolved its checks verdict. It is stored on `ReviewIssue` (`VerifiedHeadSha`) and carried on `ChecksVerdictResolved.HeadSha` for every outcome. When the review pass reads a different head, or the stored value is null (rows that predate the field), the issue returns to `checks_pending`. Because the status read runs before the checks read, the stored commit always equals or precedes the commit the verdict actually describes, so a mismatch errs toward re-checking. On an unchanged head, a review issue whose stored `ChecksOutcome` is not `Passed` (`still_failing`, `infrastructure_failed`, `unfinished`, `unreadable`, `none_detected`) has its checks read again by the review pass, after the feedback and head-moved checks and skipped while feedback is held back; only a green read returns it to `checks_pending` (a rerun of the same commits, [ADR 0108](docs/adr/0108-a-review-issue-whose-checks-pass-on-a-rerun-returns-to-the-checks-gate.md)). A `Passed` verdict is never re-read.

### Checks Pending Reason

**Checks Pending Reason** — why an issue is in `checks_pending`: `AfterRun` (a worker run or revision left an open PR, via the post-run rebase), `HeadMoved` (new commits reached a review issue's PR after its verdict), `ChecksRerun` (discriminator `checks_rerun`: a review issue whose verdict was not `Passed` read green on the same commits after a rerun) or `CatchUp` (discriminator `catch_up`: a [catch-up](#catch-up) rebase brought the branch up to date, and the checks run again before it merges; its [catch-up queue](#catch-up-queue) entry keeps its place). A closed union on `ChecksPendingIssue`. It is shown in the pane callout and does not change gate behaviour.


## Unchanged Issue

A lifecycle state for an issue whose worker completed successfully (exit code 0) but produced no code changes — no branch, no PR.
Carries `WorkerRunId` — non-nullable.
Requires manual resolution: the user can retry (disagreeing with the worker's assessment).
Classified as Active / Needs you (see [Issue Lifecycle Partition](#issue-lifecycle-partition)) — it cannot resolve itself.
Transitions: `UnchangedIssue.Retry()` → `FreshQueuedIssue`.
Hard-deleted when the provider-side issue is closed or loses its trigger label (untracked by the poller). If reopened upstream with the trigger label it is re-detected as a new issue.

## Review Queued Issue

`review_queued`: a green pull request waiting for a worker to review it ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). A [Queued Issue](#queued-issue) with `TierRank = 1`, behind revisions and ahead of every other run. Stage Review, [Awaits dispatch](#awaits-dispatch), [Run Kind](#run-kind) `Review`; its `Context` is `DispatchContext.Review(BranchName, PullRequestUrl, ReviewedCommit, CarriedPatchId, ChecksPassed)` (`ChecksPassed` is `ChecksOutcome is Passed`, see [Review Facts](#review-facts)) and its `DispatchBranchName` is null. Display group: Queued.
Entered only from `ChecksPendingIssue.ResolveVerdict` (`Passed` or `NoneDetected`, a known head, and [Review Pull Requests](#review-pull-requests) on; raises `IssueReviewQueued`), from `ReviewFailedIssue.Retry()`, or from `ReviewIssue.RetryReview()` (a person's Retry review of a [skipped](#review-outcome) review). Carries what the checks gate resolved, which every review-step state shares (`IReviewStepState`): `WorkerRunId`, `BranchName`, `PullRequestUrl`, `FeedbackCutoffAt`, `ChecksOutcome`, `RebaseOutcome`, `RebaseConflictAttempts`, `ReviewedCommit` (the [Verified Head](#verified-head), column `reviewed_commit`) and `AutoMerge` (the repository's [Merge When Checks Pass](#merge-when-checks-pass) when the gate resolved, column `review_auto_merge`).
Transitions: `Claim()` → `ReviewInProgressIssue`; `Complete()` → `CompletedIssue` (provider issue closed: a person merged the pull request with a closing keyword); `Fail()` → `ContinuableFailedIssue` (`PrClosed`, pull request closed without merge), like `ReviewIssue.Fail()`. Not a resting state, unlike the other queued states: its pull request is open, so untracking preserves it and the review pass detects its closure.

## Review In Progress Issue

`review_in_progress`: a review worker is judging the pull request at `ReviewedCommit` ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). Stage Review, [Run Kind](#run-kind) `Review`. Its `WorkerRunId` is the review run. It holds a worker slot like any running worker and counts against the Repository Worker Limit (`InFlightWorkerCountQuery`). Display group: In flow.
Leaves by its run:

- **`ReviewRunCompleted`** with the same run id (a replay guard: any other run or state is ignored) → `ResolveVerdict(outcome, findings)`: `ChangesRequested` with [Review Fix Attempts](#review-fix-attempts) below 2 → a `RevisionQueuedIssue` with a `ReviewFindings` trigger (a findings fix); `Approved` with `ChecksOutcome` `Passed`, `AutoMerge` on and no [person feedback](#has-person-feedback) → `MergePendingIssue` pinned to `ReviewedCommit`; every other outcome (`Approved` otherwise, `ApprovedForPerson`, `ChangesRequested` at the cap) → `ReviewIssue`. A pull request without checks (`NoneDetected`) never merges itself, approved or not ([ADR 0105](docs/adr/0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)).
- **`WorkerRunFailed`** with the same run id → `MarkRunFailed`: a category with an [automatic retry](#transient-retry) whose streak, this failure included, is short of its maximum → `ReviewFailedIssue`; any other failure, and an exhausted streak → `ReviewIssue` with [Review Outcome](#review-outcome) `Skipped(<category>)`. The handler reads the streak, so `review_failed` only ever holds an issue with a retry left. No failure reaches `merge_pending`: a failed review fails open to a person.

`Complete()` and `Fail()` follow closure as for `review_queued`.

## Review Failed Issue

`review_failed`: a review run failed transiently with an automatic retry left ([ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). Stage Review, never Needs action, [Run Kind](#run-kind) null; carries the review-step fields plus `FailureReason`, `FailureCategory` and `FailedAt` (`IAutoRetryableFailedState`). Display group: In flow.
Transitions: `Retry()` → `ReviewQueuedIssue` for the same commit, run by [`TransientRetryService`](#transient-retry) once the category's backoff elapses; `Complete()` and `Fail()` follow closure as for `review_queued`. It has no manual action: its automatic retry is its exit.

## Fresh Queued Issue

The first-time dispatch tier — the lowest-priority of the queued variants (`TierRank = 4`).
An issue reaches this state from four paths: a ready triage verdict or Skip triage with no open blockers, an unblocked `BlockedIssue` (all blockers resolved; it is not triaged again), or a retried `FailedIssue` / `UnchangedIssue`.
`DispatchBranchName` is generated fresh from the kind classification, number, and title (`BranchName.Generate(KindClassification, IssueNumber, Title)`; an `Unclassified` issue gets `feat/`).
`Context` is `DispatchContext.Fresh(branchName)` — instructs the worker to start a new implementation on a new branch. Its [Run Kind](#run-kind) is `Fresh`.
Transitions: `Claim()` → `InProgressIssue` (worker assigned); `Block()` → `BlockedIssue` (blockers detected); `Retriage()` → `TriageQueuedIssue` ([Re-triage](#re-triage)).

## Revision Queued Issue

A lifecycle state for an issue queued for a revision run on its existing pull request.
Carries `BranchName`, `PullRequestUrl` and `Trigger` (a [`RevisionTrigger`](#revision-trigger): review feedback, failed checks, a rebase conflict or provided input), plus `RebaseConflictAttempts` and a nullable `PriorFeedbackCutoffAt`.
Created by `Revise()` on `ReviewIssue`, `ChecksPendingIssue` and `MergePendingIssue` when actionable review feedback is detected, by a review that requested changes from `ReviewInProgressIssue` (a findings fix), by a checks fix from `ChecksPendingIssue`, by a rebase conflict from `RebasePendingIssue`, by an answer from `AwaitingInputIssue`, and by a retry from `RevisionFailedIssue`, and by triage releasing an issue that holds [Open Work](#open-work) (a `SpecChanged` trigger).
Claimed with priority over regular `FreshQueuedIssue` to minimize open issue count.
Its [Run Kind](#run-kind) follows its trigger: `ReviewFix`, `ChecksFix`, `ConflictFix`, `InputResume` (`InputProvided` and `SpecChanged`) or `FindingsFix`.
Transitions: `Claim()` → `RevisionInProgressIssue`; `Block()` → `RevisionBlockedIssue` when the dependency pass or the claim-time re-check finds open blockers; `Fail()` → `ContinuableFailedIssue` (`PrClosed`) when its pull request closes without merging before the revision starts. The review pass reads the pull request of every queued or held revision for that closure, as it does for the review step.

## In-Progress Issue

`in_progress`: an issue whose implement worker is running (`InProgressIssue`). Carries `WorkerRunId` and its **origin** (`ImplementRunOrigin`, column `implement_run_origin`): `Fresh` when claimed from `FreshQueuedIssue`, `Continuation` when claimed from `ContinuationQueuedIssue` (#1060). The origin is a required argument of every factory, so no path can leave it unset, and it decides the state's [Run Kind](#run-kind): `Fresh` or `Continuation`.
`PublishBlockedIssue` and `PublishFailedIssue` keep the origin of the implement run they hold (`null` when they hold a revision, which carries its trigger instead), so Confirm false positives and Retry publish return to `in_progress` with the origin it had. Rows that existed before the column were backfilled as `Fresh`: no stored record shows whether a run in flight then was a continuation, and the tag corrects itself when that run finishes.
Transitions: `MarkRebasePending()`, `MarkAwaitingInput()`, `MarkUnchanged()`, `MarkCompleted()`, `MarkFailed()`, `MarkContinuableFailed()`, `MarkPublishFailed()`, `MarkPublishBlocked()`, `MarkBranchDiverged()`.

## Revision Blocked Issue

`revision_blocked`: a queued revision whose issue has open blockers ([ADR 0124](docs/adr/0124-open-blockers-hold-every-implement-run.md)). It carries everything the `RevisionQueuedIssue` carried (`BranchName`, `PullRequestUrl`, `Trigger`, `PriorFeedbackCutoffAt`, `RebaseConflictAttempts`, Continued without answer) and its blockers, so unblocking loses nothing. Not a [Queued Issue](#queued-issue): never a dispatch candidate, holds no worker slot. [Waits on blockers](#waits-on-blockers). Its [Run Kind](#run-kind) follows its trigger.

- **Ways in** — `RevisionQueuedIssue.Block()`, raising `IssueRevisionBlocked`, or triage releasing an issue that holds [Open Work](#open-work) while it has open blockers (a `SpecChanged` trigger, raised on the triaged issue).
- **Ways out** — `Unblock()` → `RevisionQueuedIssue` (only), raising `IssueRevisionQueued`, when its blockers close or are unlinked; `Fail()` → `ContinuableFailedIssue` (`PrClosed`, no `WorkerRunId`) when its pull request closes without merging. A closed pull request wins over blockers clearing in the same poll. Hard-deleted on untrack. No operator actions.

## Revision In-Progress Issue

A lifecycle state for an issue whose worker is executing a revision cycle.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl` and `Trigger`, plus the fix and conflict counters and `PriorFeedbackCutoffAt` carried from `RevisionQueuedIssue`.
Created from `RevisionQueuedIssue.Claim()`. Its [Run Kind](#run-kind) follows its trigger, as when it was queued.
Transitions: `MarkRebasePending()` → `RebasePendingIssue` (worker pushed changes); `MarkUnchangedRebasePending()` → `RebasePendingIssue` (worker made no changes but the PR still exists); `MarkAwaitingInput()` → `AwaitingInputIssue`; `MarkBranchDiverged()` → `BranchDivergedIssue`; `MarkPublishFailed()` → `PublishFailedIssue`; `MarkPublishBlocked()` → `PublishBlockedIssue`; `MarkFailed()` → `RevisionFailedIssue`.

## Completed Issue

Terminal lifecycle state — the issue is resolved via a merged PR.
Carries `BranchName`, `PullRequestUrl`, and `CompletedAt` — all non-nullable.
Created two ways:

- **Provider issue closed** (`ProviderIssueClosed`) — from `ReviewIssue`, `ChecksPendingIssue`, `RebasePendingIssue`, `MergePendingIssue`, `AwaitingInputIssue`, `PublishBlockedIssue`, or `PublishFailedIssue` via each state's `Complete()`. A publish-blocked or publish-failed issue keeps its branch and its pull request (empty for an implement run, the revised pull request for a revision), and its completion also raises `IssueClosedWithUnpublishedRun` carrying its `WorkerRunId`, so Workers discards the run.
- **Foundry merged the pull request** (`MergeResolved` with `merged`) — from `MergePendingIssue.Complete()` via `CompletedIssue.FromMergePending`, keeping its branch and pull request ([Merge Pending Issue](#merge-pending-issue)). A provider that does not auto-close the issue leaves it open on the provider; `completed` is preserved by untracking.
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
Carries `BranchName`, `FailureReason`, `FailureCategory`, and `FailedAt` — all non-nullable — and `WorkerRunId`, the run whose work failed, which is absent only when a queued or held revision's pull request closed before any run started.
Optionally carries `PullRequestUrl` — present when created from `ReviewIssue.Fail()` (PR was closed without merge), absent when created from `InProgressIssue` (no PR existed).
Created from `InProgressIssue` when the failed run left commits on the branch — Foundry checks via `GetBranchCommitSummaryAsync` against the provider after the container exits; commit count > 0 is the branch-has-commits boolean.
Also created from `ReviewIssue.Fail()` since `ReviewIssue` always has a branch.
Retry dispatches a continuation run that checks out the existing branch and resumes implementation.
Transitions: `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch); `ContinuableFailedIssue.RetryFresh()` → `FreshQueuedIssue` (discards the branch work — see Operator-Triggered Fresh Retry).

## Continuation Queued Issue

A lifecycle state for an issue queued for continuation from an existing branch with prior work.
Carries `BranchName` and `FailureReason` — both non-nullable. `FailureReason` is copied from the originating `ContinuableFailedIssue` and truncated to 500 characters by the aggregate.
Created from `ContinuableFailedIssue.Retry()`.
Its [Run Kind](#run-kind) is `Continuation`.
Transitions: `Claim()` → `InProgressIssue` (reuses the existing in-progress variant with origin `Continuation`; the continuation context lives in the dispatch payload); `Block()` → `ContinuationBlockedIssue` when the dependency pass or the claim-time re-check finds open blockers.

## Continuation Blocked Issue

`continuation_blocked`: a queued continuation whose issue has open blockers ([ADR 0124](docs/adr/0124-open-blockers-hold-every-implement-run.md)). It carries the `BranchName`, `FailureReason` and Continued without answer of the `ContinuationQueuedIssue` and its blockers. Not a [Queued Issue](#queued-issue). [Waits on blockers](#waits-on-blockers). [Run Kind](#run-kind) `Continuation`. It carries no pull request, so a closed pull request does not move it.

- **Ways in** — `ContinuationQueuedIssue.Block()`, raising `IssueContinuationBlocked`.
- **Ways out** — `Unblock()` → `ContinuationQueuedIssue` (only), raising `IssueContinuationQueued`. Hard-deleted on untrack. No operator actions.

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
Transitions: `Retry()` → `RevisionQueuedIssue` (re-enters revision path with existing branch), by an operator or automatically: a failure whose category is auto-retryable (`transient_api_error`, `provider_unreachable`, `fetch_failed`, `package_source_unreachable`, `package_restore_failed`) is retried by [`TransientRetryService`](#transient-retry) on that category's schedule. Any other category waits for an operator.

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
| `fetch_failed` | 5 | 2, 4, 8, 16, 32 min |
| `package_source_unreachable` | 5 | 2, 4, 8, 16, 32 min |
| `package_restore_failed` | 5 | 2, 4, 8, 16, 32 min |

Each tick, it loads `FailedIssue`, `ContinuableFailedIssue`, `TriageFailedIssue`, `RevisionFailedIssue` and `ReviewFailedIssue` candidates (each implements `IAutoRetryableFailedState`) whose `failure_category` is in `FailureCategory.AutoRetryable` (translated to SQL `IN (…)`), with a coarse `FailedAt <= now - shortest first backoff` prefilter, then decides per candidate in memory against the schedule of the issue's **current** category (the latest failure): the streak is the number of leading consecutive failed runs whose category is auto-retryable, of any such category, derived from the append-only `worker_runs` rows (`IWorkerRunQueries.CountConsecutiveTransientRunsAsync`, bounded at maximum + 1 materialized rows); the streak breaks on the first run that is not one (a completed run, an active run, or any other failure). The issue is exhausted when the streak reaches the maximum, and otherwise due when `FailedAt + BackoffAfter(streak)` has elapsed — the backoff of the next retry, the streak's n-th backoff.
Because the latest category's maximum applies to the whole streak, a mixed streak stops on the stricter schedule when a `transient_api_error` follows an unreachable provider (U, E: 2 ≥ 2) and keeps going when an unreachable provider follows an API fault (E, U: 2 < 5). The streak can never exceed the largest maximum, so the loop stays bounded.
Due candidates are re-queued through the existing transition — `FailedIssue.Retry()` → `FreshQueuedIssue`, `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue`, `TriageFailedIssue.RetryTriage()` → `TriageQueuedIssue`, `RevisionFailedIssue.Retry()` → `RevisionQueuedIssue`, `ReviewFailedIssue.Retry()` → `ReviewQueuedIssue` — after reloading the issue, so a de-labelled or already-retried issue is a safe no-op (a concurrent manual retry that moved the issue out of the failed state, or won the `DbUpdateConcurrencyException` race, is tolerated).
Schedules are hardcoded (no settings surface). An exhausted issue stays failed, leaving `POST /api/issues/{id}/retry` as the manual escape hatch. A review run never waits exhausted: the `WorkerRunFailed` handler reads the streak and sends a failure with no retry left straight to `review` with `ReviewOutcome.Skipped` ([Review In Progress Issue](#review-in-progress-issue)). The issue detail's "Attempt n of N" block (`TransientRetryDetails`) applies to the same four states and categories, with N the current category's maximum.
Due-ness is recomputed from the persisted `FailedAt` on every tick, so a host restart during a backoff window costs at most one extra tick of delay — no in-memory timer is required.

## Review Comment

A value object representing a single piece of reviewer feedback on a PR.
Carries `Body` (text), optional `FilePath`, and optional `Line` number.
Lives in Monitoring contracts (`Foundry.Modules.Monitoring.Contracts`) — produced by the monitoring provider, carried into a revision by `RevisionTrigger.ReviewFeedback`, and rendered by the worker dispatch.

## Review Feedback

A provider-agnostic result from `IIssueProvider.GetReviewFeedbackAsync()`.
Carries an `IReadOnlyList<ReviewComment>`, an `OmittedCommentCount`, and the `NewestCommentAt` timestamp of the newest kept comment.
The actionability rule is provider-agnostic and lives entirely in `ActionableFeedbackPolicy` (see [ADR 0070](docs/adr/0070-any-human-comment-is-actionable-feedback.md)): every human-authored, non-system comment created after `FeedbackCutoffAt` is actionable unless it sits in a resolved review thread — regardless of review state or provider.
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

The revision variant of the [Dispatch Context](#dispatch-context), `DispatchContext.Revision`.
Carries `BranchName`, `PullRequestUrl` and the `RevisionTrigger`, whose `ReviewFeedback` variant holds the `ReviewComment` list.
Built by `RevisionQueuedIssue.Context` and carried on `ClaimedIssueDispatch`; fresh, continuation and triage claims carry their own variants.
The run's base bundle includes the existing branch, and `SystemPromptBuilder` renders the trigger's own section (review feedback, failing-job logs, a rebase-conflict instruction, or the provided input).

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
The `ProviderIssueUntrackedHandler` hard-deletes tracked records in resting states (`Issue.IsRestingState()`): `detected`, `triage_queued`, `queued`, `revision_queued`, `continuation_queued`, `blocked`, `triage_blocked`, `revision_blocked`, `continuation_blocked`, `failed`, `triage_failed`, `continuable_failed`, `revision_failed`, `branch_diverged`, and `unchanged`.
`completed` is preserved — completion wins over provider closure.
`triage_in_progress`, `in_progress`, and `revision_in_progress` are preserved — a live worker is running. Untracking never stops a worker; stopping one is an explicit user action (see Run Cancellation).
`review`, `checks_pending`, `rebase_pending`, `merge_pending`, `awaiting_input`, `publish_failed`, and `publish_blocked` are preserved — each asks the provider whether the issue is closed (`ProviderIssueClosed`) and completes when it is.
For `publish_failed` and `publish_blocked` that close check runs in the listing pass, and only for an issue missing from a complete listing: absence alone cannot tell a closed issue from a removed trigger label, and an issue still in the listing is not asked about. Their completion raises `IssueClosedWithUnpublishedRun`, and Workers discards the run and deletes its Hand-off volume. Until then their run's unpublished work waits in that volume, and an issue still open leaves only when the run is discarded or the volume's time to live expires.
An issue closed on the provider and later reopened with the trigger label is re-detected as a new issue.

## Provider Issue

A DTO representing an issue as returned by a provider's API.
Carries raw data (number, title, author username, URL, labels) that the domain maps into value objects and aggregates during detection.

## Repository Slug

The full namespace path that identifies a repository within a provider — two or more `/`-separated segments (`owner/name` on GitHub, `group/subgroup/project` for nested GitLab groups).
Value object — `Name` is the last segment, `Owner` is everything before it (a multi-segment namespace path for GitLab). The provider adapter renders it as needed: GitHub as `owner/name` path segments, GitLab as a URL-encoded full path.

## Worker Role

The job a worker run is dispatched to do ([ADR 0095](docs/adr/0095-worker-roles-ship-in-the-worker-image.md)): `triage` (judges whether an issue is ready to implement and classifies it), `implement`, and `review` (judges whether an implementation pull request is correct and meets its issue; dispatched after the checks gate, see [ADR 0113](docs/adr/0113-review-judges-the-verified-head-after-the-checks-gate-and-code-derives-its-outcome.md)). Every new issue is dispatched to `triage` before its first `implement` run (see [TriageQueuedIssue](#triagequeuedissue)).
Each role brings its own Claude Code plugin (skills, agents), prompt and an optional verdict schema from `workers/roles/<role>/` (plus `workers/roles/shared/`, loaded for every role). These folders are baked into the worker image at `/opt/foundry/roles/`.
Foundry's code picks the role, starts the run and acts on the verdict ([ADR 0094](docs/adr/0094-code-decides-claude-judges-only-what-code-cannot.md)). A WorkerRun has exactly one role. Fresh, revision, continuation and rebase-conflict runs all use `implement`. Their context sections stay in `SystemPromptBuilder`.
"Step", "stage" and "phase" already have other meanings here, so they are not used for this concept.

- **Folder layout**: `.claude-plugin/plugin.json` (its `description` is the role's purpose), `shared/prompts/principles.md` (the Worker Principles framing line) and `shared/prompts/principles/<section>.md` (one file per Principle Section; all required, no placeholders), `prompts/system.md` (appended after Foundry's safety preamble and the role's Worker Principles; placeholders `{issueNumber}` and `{issueContent}`), `prompts/task.md` (the `claude -p` task; must contain `{issueNumber}`), optional `skills/<skill>/SKILL.md`, `agents/<agent>.md` and `verdict.schema.json`.
- **Worker Principles**: the engineering standards under `workers/roles/shared/prompts/`. The framing file `principles.md` holds one line that introduces them; the standards themselves are grouped into Principle Sections, one file each under `principles/`. Each Worker Role declares in code which sections it receives, and its system prompt carries the framing line followed by those sections, after the safety preamble and before the role prompt (implement and review: all four, so the reviewer never judges against a rule the implementer did not receive; triage: Design and Domain modelling). Each bullet carries a **Principle Id** `<section>.<n>` (`testing.1`, `design.1`, `code.1`, `domain.1`, …; `n` is the bullet's 1-based position in its file after the heading line), rendered as `- [design.1] <text>`. Ids are computed when the principles are assembled, never written in the files, so the prompts and the role cards always show the same ids; a review finding cites one as its rule. Inserting a line renumbers later ids for future runs only. They are worded as properties of code, so each role applies, judges against, or designs to them as its own prompt says. Unlike the safety preamble, they yield to the target repository's style, testing and design conventions, and never to the safety rules. Every file is required and takes no placeholders. A missing or blank file, one containing a `{placeholder}`, or a framing line plus all four sections over 8 KiB (UTF-8 bytes) together fails the unit tests and the startup image build.
- **Principle Section**: a named group of Worker Principles (Testing, Design, Code, Domain modelling), held in one file at `workers/roles/shared/prompts/principles/<section>.md`; each Worker Role declares in code which sections its system prompt carries. The sections follow the shared framing line in the section type's fixed order, and a role with no sections gets neither the framing nor any principles.
- **Names come from code**: `WorkerRole.All` lists the dispatchable roles, and each role declares its capability and its Principle Sections there. A role without a folder, or a role prompt using a placeholder Foundry does not fill, fails the unit tests and the startup image build, as does a missing, blank or placeholder-bearing framing or section file, or principles over 8 KiB combined. The entrypoint receives the role as `WORKER_ROLE` and loads `/opt/foundry/roles/<role>` and `/opt/foundry/roles/shared` with `--plugin-dir`. A role this image does not carry fails bootstrap at stage `role`.
- **Role Catalog**: the startup image build reads every role's manifest, skills, agents, prompts and verdict schema, plus the shared Worker Principles framing and section files, from the same build-context tar it sends to Docker, and holds the catalog in memory (`IWorkerRoleCatalogStore`). Dispatch takes the role's prompts and its assembled Worker Principles from it, and `GET /api/worker-roles` serves it to the dashboard's role cards (read-only except the Role Model) together with the `SystemPromptBuilder` section metadata (each section in assembly order, tagged with when it is included; the role prompt and the Worker Principles stand as `{rolePrompt}` and `{principles}` slots, the others are hardcoded). Each card carries its role's selected Principle Section names and assembled principles text, and the catalog carries every section name in fixed order. With image build disabled (operator-supplied tag), the catalog is read from disk and marked unverified against the running image. A role change merged while Foundry runs takes effect at the next restart, and until then the cards keep showing what the workers actually run.
- **Capability profile** ([ADR 0098](docs/adr/0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md)): every role declares a `WorkerCapability` in code (`WorkerRole`, carried by `WorkerRoleDefinition`; never read from its folder), `Restricted` or `Full`. `implement` is `Full`, `triage` is `Restricted`. The container spec, the entrypoint (`WORKER_CAPABILITY=restricted|full`) and `SystemPromptBuilder` branch on the profile, never on the role name. No run of either profile gets a provider token: the entrypoint unsets `GH_TOKEN`, `GITLAB_TOKEN` and `GIT_PAT`, and every run clones a base bundle the Publisher prepared ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)). For every run, Foundry reads the issue title, body and comments (`IIssueContentQueries`, through `IIssueProvider.GetIssueContentAsync`) into `{issueContent}` as XML-encoded untrusted data, dropping the oldest whole comments first when the prompt budget runs out. A `Full` run gets a branch pre-created on the provider, its branch in the base bundle when it resumes one (continuation or revision), and `--dangerously-skip-permissions`. A `Restricted` run gets no branch: its base bundle holds only the default branch, and `claude` runs with `--restricted --strict-mcp-config` and never the permission skip. Its system prompt carries restricted safety rules and none of the implement-only sections (pre-PR, checks, fresh, revision, continuation). A failed issue read fails the run before any container starts.
- **Recorded on the run**: every WorkerRun stores its `Role` (column `role`) and the `Image` it was dispatched on (column `image`, `tag@image-id` when the image ID is known).
- **Verdict**: a role with `verdict.schema.json` returns a schema-validated verdict. The entrypoint merges the `$defs` of `workers/roles/shared/verdict.defs.json` (e.g. the shared `questions` definition) into the schema and passes it to both `claude` invocations with `--json-schema`. Foundry reads the last result event's `structured_output` and parses it with the role's typed verdict parser (`IWorkerVerdictParser`; `implement`: `{outcome: done | needs_input, questions}`; `triage`: `{outcome: ready | needs_input | blocked, kind: feature | bug | refactor | documentation, questions, decisions, blockedBy}`, recorded as `done` for `ready`; `decisions` is optional beside `ready` and `needs_input` and refused beside `blocked`, each `{question, decision, ground: settled_business | implementation_detail, evidence}` read by `VerdictDecisionsReader`, [ADR 0123](docs/adr/0123-workers-ask-only-about-uncertain-business-logic-and-decide-the-rest.md)). For a run that exits 0, the terminal run records `VerdictStatus` (column `verdict_status`: `done`, `needs_input`, `missing` or `invalid`) and, for `needs_input`, its `VerdictQuestions` (column `verdict_questions`). An `implement` run's outcome is decided from facts, except that `needs_input` moves the issue to `AwaitingInputIssue` ([ADR 0096](docs/adr/0096-awaiting-input-replies-detected-by-recorded-ids-and-branch-head.md)). A `triage` verdict decides the issue's next state (see [TriageInProgressIssue](#triageinprogressissue)), and a missing or invalid one fails the run as `verdict_invalid`.
- **Role Model**: the one per-role operator setting, stored in `worker_role_settings` keyed by role name (`WorkerRoleSettings`). Migrations seed `triage` and `review` with `opus`; `implement` has no seeded row, so it runs on the account default. The operator changes or clears any role model. It is empty (the CLI's account default; no `model` is written to the managed settings), a documented Claude Code alias (`fable`, `opus`, `sonnet`, `haiku`, `opus[1m]`, `sonnet[1m]`; the code constant `ClaudeModelAliases`), or a model ID listed by the Anthropic Models API. There is no global model and no fallback chain. Dispatch writes the role's model into the managed settings `model`.
- **Model List**: `IClaudeModelCatalog` fetches `GET https://api.anthropic.com/v1/models?limit=100` (paged with `after_id` while `has_more`, reading `id`, `display_name` and `lifecycle`) with the credential workers use: the API key (`x-api-key`) or the OAuth access token read from the credential volume through the credential-helper container (`Authorization: Bearer`). The list is cached in memory; `GET /api/worker-roles/models?refresh=true` refreshes it when the role view opens. A failed refresh keeps the previous list and reports the error, and the role card still offers Default, the aliases and the saved value.
- **Saving a model**: `PUT /api/worker-roles/{role}/model` accepts empty, an alias, or an ID in the current list, and otherwise rejects it with `Worker.RoleModelNotListed`. A saved model the list later drops is shown as unavailable and dispatch still uses it.
- **Model Unavailable**: the CLI validates the model only on its first request. A run whose result line reports "There's an issue with the selected model" fails with `FailureReason.ModelUnavailable` (category `model_unavailable`), whose summary names the role whose model setting to change.

## WorkerRun

A single execution of a worker against an issue — the primary aggregate for the Workers module.
Modeled as a polymorphic aggregate with state variants: `StartingRun` (container creation requested), `ActiveRun` (container running), `PublishingRun` (container ended; its commits are being published), `PublishFailedRun` (a publish attempt failed), `CompletedRun` (exited successfully), `FailedRun` (exited with error, timed out, failed to start, or was discarded).

**Publishing** ([ADR 0100](docs/adr/0100-workers-hold-no-provider-credential-and-foundry-publishes.md)). A run with a branch moves `Running → Publishing → Completed` (or `Failed`): when its container exits, times out, is cancelled or is found gone, `ActiveRun.BeginPublishing` keeps the exit facts, container log tail, verdict and the verdict's raw `pullRequest` text on a `PublishingRun`, and the container is removed while the Hand-off volume stays. `RunPublishingService` then runs `WorkerRunPublisher`: the Publisher pushes the Run bundle; for a run that exited 0 without a `needs_input` verdict, code opens the pull request when none is open (title `<type>: <summary>` from the issue's kind and the verdict's validated summary, else the issue title; body laid out by `PullRequestText` from the verdict's structured `PullRequestDescription`: one `[!WARNING]` alert for the diff gate's counts and the run's `attention` items, an optional **Cause:**, **Changes** and **Verified** bullets, then collapsed implementation notes and diff-gate lists, with the commit subjects as **Changes** when there is no valid description, then `Closes #<issue>`), or posts one summary comment on the open one in the same layout under a heading naming the issue's run kind (recorded in the posted-comment registry; the body is never overwritten); then the outcome is resolved and applied as before. Each step is recorded (`published_at`, `pull_request_settled_at`), so a retry never pushes, opens or comments twice. `WorkerRunCompleted` carries the publish facts (`RunPublishFacts`: pushed head, pull request URL, diverged side branch and its [divergence reason](#branch-divergence-reason), branch commit count). A publishing run holds no [Worker Slot](#worker-slot): its slot was freed when its container stopped.
**PublishFailed.** A transient publish failure (including `kind=unreachable`) moves the run to `PublishFailedRun`, retried after `PublishRetryPolicy.RetryDelays` (1, 2, 5, 15 minutes; `MaxAttempts` 5); while the run's host is paused ([Host Pause](#host-pause)) a due retry waits without spending an attempt. While it waits to retry it holds no [Worker Slot](#worker-slot). A permanent failure, or the last attempt, exhausts it: the run raises `WorkerRunFailed` with category `publish_failed`, and its issue waits in Needs you with **Retry publish** (`POST /api/workers/runs/{id}/publish-retry`: `202`, `409` when the run is not waiting for a publish retry, `404`), which reruns Publish from the retained Hand-off volume without taking a slot. A diverged run completes with its side branch and divergence reason, and its issue waits in Needs you as branch diverged.
**PublishBlocked.** A run whose publish the [Diff gate](#diff-gate) blocked on a provider-pattern finding raises `WorkerRunPublishBlocked` (the provider-finding count only; a generic finding never blocks); like every `UnpublishedRun` it holds no [Worker Slot](#worker-slot), and its issue waits as `publish_blocked`. **Discard run** (`POST /api/workers/runs/{id}/discard`: `202`, `409` when the run is not holding unpublished work that waits for an operator, `404`) ends a blocked run or an exhausted publish-failed run: the run fails with category `run_discarded`, its Hand-off volume is deleted, and its issue fails, so Retry Issue starts a new run from the branch. The Hand-off time to live discards a blocked run like a publish-failed one. **Confirm false positives** (`POST /api/workers/runs/{id}/confirm-false-positives` with `{ "publishBlockedAt": <the run detail's publishBlockedAt> }`: `202`, `409` when the run is not `publish_blocked` or was blocked again since the client rendered it, `404`, `400` when `publishBlockedAt` is missing) returns a blocked run to `PublishingRun` without taking a slot and raises `WorkerRunFalsePositivesConfirmed`, which returns its issue to `in_progress` or `revision_in_progress`.
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

## Worker Slot

One unit of concurrent Claude token use, bounded by `MaxConcurrent`.
A run holds a slot from its [Dispatch Reservation](#dispatch-reservation) until its `claude` container stops: as a `StartingRun` and as an `ActiveRun`. The rule is `WorkerRun.HoldsSlot`.
Publishing, retrying a publish, and any other `UnpublishedRun` state hold no slot, because the Publisher uses no tokens; a run an operator returns to `PublishingRun` (**Retry publish**, **Confirm false positives**) takes none either.
Token use outside a run, such as the usage poll's `claude -p "/usage"`, holds none.
The [Repository Worker Limit](#repository-worker-limit) counts the same thing per repository: in-flight issues minus those whose run is an `UnpublishedRun`.

## Worker Slot Occupancy

The count of dispatch slots held at any moment — the figure checked against `MaxConcurrent` before a new run is dispatched.
Slot occupancy is the union of reservations, starting and active runs (`reservations ∪ starting ∪ active`): a run holds its [Worker Slot](#worker-slot) until its `claude` container stops (`WorkerRun.HoldsSlot`). A publishing run, or one retrying or waiting on a publish, holds none.
Including `DispatchReservation` in the count prevents over-dispatch during the window between the `WorkerCapacityAvailable` event being published and the claim being processed; including `StartingRun` prevents over-dispatch during the window between row creation and container start.
An unresolved `DispatchReservation` holds its slot for at most `StaleReservationThreshold` (2 minutes) before `StaleReservationService` deletes it.
An unresolved `StartingRun` holds its slot for at most `StaleStartingRunThreshold` (10 minutes) before `StaleStartingRunService` fails it (see Orphan Reconciliation).
The query is implemented by `DbContext.GetSlotOccupancyCountAsync` and `DbContext.GetSlotOccupancyRunIdsAsync` extension methods, used by both `WorkerDispatchService` (dispatch gate) and `StaleStartingRunService` (orphan reaping).
The same count is exposed read-only as `GET /api/workers/capacity` (`MaxConcurrent`, `OccupiedSlots`, `FreeSlots`) for the Board's in-progress band, so the dashboard never recomputes occupancy.

This occupancy count is **distinct from the per-repository cap**: `MaxConcurrent` is a global ceiling enforced Workers-side via this count; the [Repository Worker Limit](#repository-worker-limit) is a per-repository ceiling enforced Issues-side from the in-flight issues (`InProgressIssue`, `RevisionInProgressIssue`, `TriageInProgressIssue`) minus those whose run is an `UnpublishedRun`, grouped by repository.
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

After each connect the dashboard invokes `WorkerHub.GetCurrentActivity()`, which returns the `WorkerActivity` and [Agent Activity](#agent-activity) payloads of every active run, and replaces what it holds with the result.
A dashboard reload or reconnect therefore renders the current last-activity time without waiting for the next observation tick, a run that ended while the client was disconnected is dropped at once, and the client knows when it is up to date.
The hub pushes nothing on connect; live updates arrive through the same `WorkerActivity` and `AgentActivity` client methods as before.

**Dashboard display.**
Log silence is a frontend-only signal derived from the [Agent Activity](#agent-activity) `LastEventAt` — the time of the last stream-json event the agent emitted — shown only past a 5-minute threshold. While a run publishes, the card shows `Publishing commits` (or `Publish failed, retrying`) instead of the activity line.
Because `LastEventAt` advances only when the agent itself produces output (including `system/thinking_tokens`), the badge detects an agent that is alive but idle; when a tool is in flight the badge names it (e.g. `silent 6m · Bash running`) so a long-running test suite is not mistaken for a hang.
Until the first Agent Activity payload arrives (e.g. during the SignalR handshake) the card falls back to `LastActivityAt`, which only lapses when observation itself stops.

## Agent Activity

A live, in-memory projection of a running worker's stream-json events, one per `ActiveRun` — the agent's own view of its progress, read without a Foundry-defined stage vocabulary.
It holds the agent's task list (`TaskCreate` / `TaskUpdate`: subject, `activeForm`, status, with task ids correlated from the `TaskCreate` tool results), the current in-progress task and "N of M" completed, the active subagents (`subagent_type`, description, last tool — parallel subagents tracked separately, each removed when its `task_notification` reports completion), the in-flight tool (the name of the latest `tool_use` with no matching `tool_result`), the background tasks (each `task_type` and redacted, collapsed `description`, usually a backgrounded shell command — replaced as a whole by every `system/background_tasks_changed` event, which carries the full live set), token totals (input, output, cache read, cache creation — summed once per `message.id`, since a message is emitted once per content block with identical `usage`), and `LastEventAt`.
Cost is not estimated live; it arrives with the final `result` event.

**Follower.**
`AgentActivityFollowerService` (not `WorkerDispatchService`) reconciles a log follower per `ActiveRun` every 10 s and flushes changed projections every 2 s.
Each follower reads the container log from the start, so a Foundry restart rebuilds the projection by re-attaching (cost proportional to run length — accepted).
A repeated `system/init` or the `claude -c` transient retry continues the projection rather than resetting it; a `result` event clears the in-flight tool and subagents but keeps tasks, token totals and background tasks, because background tasks outlive the turn that started them.
When the run leaves `ActiveRun` its follower is cancelled, its projection disposed, and an `AgentActivityEnded` payload is broadcast; a follower whose log stream faults is re-attached with a fresh projection on the next reconcile.

**Broadcast and replay.**
Changed snapshots are broadcast as an `AgentActivity` payload via the `WorkerHub` `AgentActivity` client method, and `WorkerHub.GetCurrentActivity()` returns the current payload for every active run to a client that has just connected ([ADR 0063](docs/adr/0063-replay-live-worker-state-over-signalr-instead-of-the-issue-read-model.md), [replay](#signalr-worker-activity-replay)).
The projection is presentation-only: it is never persisted, and text fields are redacted summaries — never tool result bodies.

**Dashboard display.**
The in-progress issue card shows one line such as `developer · Running tests (3/7)`: the latest active subagent type (or `main agent`), the current task `activeForm`, and task progress; a run with no task list shows the active subagent and the in-flight tool instead.
Past the 5-minute silence threshold, a run with background tasks is *waiting*, not silent: the worker card footer reads `waiting on <first description> +N more · 7m` in neutral styling (an empty description reads `a background task`; screen readers hear only the count), the issue pane heading reads `waiting 7m` above a list of every task's full description, and no silence episode starts, so the Board does not announce `worker silent`. Once the tasks finish and the run stays quiet, a normal silence episode follows.
The worker system prompt carries one workflow-neutral line asking the agent to track its plan with its task list and keep it current.

## Active-Agents Chip

The top bar shows `● N agents working` (the count of active runs reporting Agent Activity), or an idle state at zero, linking to the Board.
Count changes are announced through the shell's single polite announcer; an open issue pane announces its run's step changes politely, and token or clock ticks are never announced.

## Connection Gate

The dashboard's guard against showing live data that may be out of date. The Board is gated; Settings and History are not.
When any of the three SignalR hubs (issues, system, workers) has been down for 2 s, the gated page is destroyed and a reconnect screen takes its place; a drop that recovers within those 2 s leaves the page as it is.
Each hub retries on its own, with no limit: at once, then after 1, 2, 5 and 10 s, then every 30 s ± 20 %; **Try now** cuts the wait short.
Once every hub is back the page refetches what it shows (Board: issues, capacity, dispatch settings and repositories) and renders afresh only when that has loaded; a failed refetch is retried on the same schedule.
While the gate is up the top bar's agent count and usage show placeholders instead of their last-known values.

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
Variants: `NonZeroExit(exitCode)` (container exited with non-zero code), `TimedOut` (exceeded configured timeout), `ContainerError(message)` (Docker-level failure — image not found, daemon unavailable, etc.), `UsageLimited(resetsAt)` (worker hit a *time-based* Anthropic API usage limit — session, weekly, or Opus quota — carrying a parseable reset time; token `usage_limited`), `CreditsExhausted` (worker hit a *money-based* 429 — credit pool empty, org monthly spend limit reached, or the CLI ≥ 2.1.119 regression — carrying no reset time; token `credits_exhausted`; summary `"Credits exhausted"`, which deliberately shares no prefix with the usage-limit summary so the auto-resume `LIKE 'Usage limit reached%'` sweep cannot catch it; blocks the account's spend indefinitely until an operator resumes), `WorkerBootstrapFailed(detail)` (pre-task failure — the worker container died during entrypoint bootstrap, before `claude` ran; carries a short, secret-redacted diagnostic `Detail` with the failed stage and error tail), `AuthInvalid` (the worker's Claude credentials were rejected — `api_error_status == 401` or `error.type == "authentication_error"`; triggers the auth-invalid pause), `TransientApiError` (a transient Anthropic API fault the run neither caused nor can fix — `api_error_status` in the 5xx range, or `is_error: true` with `api_error_status: null` and a `result` matching a known transient phrase such as a mid-response connection drop or `529 Overloaded`; token `transient_api_error`; drives the bounded auto-retry described under Transient Retry), `ProviderError(message)` (a provider API call needed to start the run failed, e.g. branch pre-creation rejected with 403; raised before the worker task begins), `ProviderUnreachable(detail)` (the [Publisher](#publisher)'s Prepare could not reach the git provider on any of its attempts — `FOUNDRY_BOOTSTRAP_FAILED stage=unreachable`; carries git's last error, secret-redacted; token `provider_unreachable`; summary `"Could not reach the git provider: <detail>"`; retried automatically on its own, longer schedule described under Transient Retry), `FetchFailed(detail)` (the [Publisher](#publisher)'s Prepare reached the git provider, but `ls-remote` or the fetch kept failing transiently through every in-script retry — `FOUNDRY_BOOTSTRAP_FAILED stage=fetch`; carries the sentinel's detail, secret-redacted, without its `fetching the repository failed: ` prefix; token `fetch_failed`; summary `"Fetching the repository failed: <detail>"`; retried automatically on the same schedule as `provider_unreachable`, and never pauses a host — see [Fetch Failed](#fetch-failed)), `PackageSourceUnreachable(detail)` (the worker's entrypoint got no HTTP response from the [Package Source Proxy](#package-source-proxy) before `claude` started — `FOUNDRY_BOOTSTRAP_FAILED stage=package-sources`; carries the sentinel's detail line, secret-redacted; token `package_source_unreachable`; summary `"Could not reach the package proxy: <detail>"`; retried automatically on the same schedule as `provider_unreachable`), `PackageRestoreFailed(detail)` (a full-capability run whose repository declares [Package Sources](#package-source) could not restore them before `claude` started — `FOUNDRY_BOOTSTRAP_FAILED stage=package-restore`; carries the failing target's workspace-relative path and the first restore output line containing `error`, secret-redacted; token `package_restore_failed`; summary `"Could not restore packages: <detail>"`; retried automatically on the same schedule as `package_source_unreachable`, and never trips the [Package Proxy Pause](#package-proxy-pause), since a restore failure can be specific to one repository), `Cancelled` (a user cancelled the run and Foundry stopped its container — see Run Cancellation; token `cancelled`; summary `"Worker run cancelled"`; never auto-retried), `IssueClosed` (the run's issue closed on the provider while its work waited unpublished, so the run was discarded; token `issue_closed`), `RepositoryRemoved` (the run's repository was removed from Foundry while its work waited unpublished, so the run was discarded — see Removed Repository; token `repository_removed`; summary `"The run was discarded because its repository was removed from Foundry: nothing was pushed"`).
`ContainerError` also carries the external-kill classifications described under Worker Outcome Detection (`"Worker ran out of memory"`, `"Worker container was terminated outside Foundry"`, `"Worker container was removed outside Foundry"`).
Each variant's `CategoryToken` value flows from the matching `FailureCategory` const (see FailureCategory below), making `FailureCategory` the single source of truth for the token vocabulary.

## FailureCategory

A value object in `Foundry.Modules.Workers.Contracts` owning the eleven-token vocabulary that names *why* a run failed, and the single owning type for that vocabulary (see [ADR 0068](docs/adr/0068-failurecategory-value-object-owns-token-vocabulary.md)).
The eleven tokens are the ten `FailureReason` category tokens (including `cancelled`) plus `pr_closed`.
`FailureReason`'s ten `CategoryToken` values derive from the matching `FailureCategory` const fields, so both producers obtain their value from the owning type: `WorkerRunFailedHandler` converts `FailureReason.CategoryToken` at the domain boundary, and `ProviderPullRequestClosedHandler` uses `FailureCategory.PrClosed` directly.
The entity property on `FailedIssue`, `ContinuableFailedIssue`, and `RevisionFailedIssue` is typed `FailureCategory` and persisted via an EF `ValueConverter` to the unchanged `TEXT` column (`failure_category`) — no migration needed, and the `TransientRetryService` predicate stays SQL-translatable by testing membership in `FailureCategory.AutoRetryable` (`IN (…)`).
The converter's read direction is lenient: unknown stored tokens coalesce defensively to `FailureCategory.NonZeroExit` without throwing, so a stale or legacy row never disables auto-retry. The domain-boundary conversion in `WorkerRunFailedHandler` remains the point that rejects and logs unknown tokens from incoming events.
The `WorkerRunFailed` contract `Category` field remains `string?`; rejection of unknown or null values happens at the domain boundary in `WorkerRunFailedHandler`, which falls back to `FailureCategory.NonZeroExit` (logging a warning) so the issue always transitions to failed.
Drives category-conditional recovery: each category carries an optional `AutomaticRetry` schedule (`transient_api_error`: 2 retries, 1 minute each; `provider_unreachable`, `fetch_failed`, `package_source_unreachable` and `package_restore_failed`: 5 retries each, 2–32 minutes doubling), and those with one are `FailureCategory.AutoRetryable` — the bounded auto-retry described under Transient Retry.

## Fetch Failed

Prepare reached the git provider, but `ls-remote` or the fetch kept failing for a reason `classify_error` reads as transient through every in-script retry. It retries automatically and does not pause the host.

The [Publisher](#publisher) prints `FOUNDRY_BOOTSTRAP_FAILED stage=fetch` when the last attempt of its provider phase was `transient`; `PrepareOutputParser` reads it as `FailureReason.FetchFailed` (category `fetch_failed`), and [`TransientRetryService`](#transient-retry) retries the issue from `triage_failed`, `failed`, `continuable_failed`, `revision_failed` or `review_failed` on the same 2, 4, 8, 16 and 32 minute schedule as `provider_unreachable`. Unlike `provider_unreachable` it stages no [Host Pause](#host-pause): `ls-remote` has just reached the host. Its badge reads `FETCH FAILED` in the warning tone of the other automatically retried infrastructure categories.

## Usage Limit

A *time-based* state where the Anthropic API quota (session, weekly, or Opus limit) is exhausted and self-heals at a known reset time.
Detected by parsing the worker container's JSON output (`--output-format json`): the primary signal is `ResultMessage.api_error_status == 429`; the `terminal_reason` allowlist (`"blocking_limit"`, `"rapid_refill_breaker"`) is retained as a secondary signal for older output shapes. Note that a 429 can arrive with `subtype: "success"` and `terminal_reason: "completed"`, so neither field is reliable on its own.
The reset time is extracted from the human-readable result text. Two wall-clock wording shapes are handled: `resets <time> (UTC)` (e.g. `"You've hit your limit · resets 12:10am (UTC)"`) and `reset at <time> (<IANA zone>)` (e.g. `"Your limit will reset at 3pm (America/New_York)."`). In both forms the time is 12-hour; a bare hour without minutes (e.g. `3pm`) defaults minutes to `00`. UTC times resolve to the next future UTC occurrence directly; IANA zone names are converted to UTC via the system timezone database, then the same roll-forward applies. ISO-8601 timestamps in the result text take precedence over wall-clock parsing.
Whether a reset time parses is the discriminator between the two 429 classes (see [ADR 0014](docs/adr/0014-remove-immediate-requeue-always-pause.md)): a 429 with a parseable reset time is a `UsageLimited` time-based limit; a 429 with no parseable reset time is a money-based block classified as `CreditsExhausted` (see FailureReason and Dispatch Pause). There is no fabricated-cooldown fallback — the former `DefaultCooldownMinutes` setting is removed.
A detected usage limit always triggers a global dispatch pause via `DispatchPause.UsageLimitResetsAt` — there is no immediate-requeue path. `DispatchPause.SetUsageLimitResetsAt` remains extend-only and clamps to 7 days, so a reset time already in the past only ever extends an existing pause.
The triggering issue transitions to `FailedIssue` / `ContinuableFailedIssue` with `FailureReason.UsageLimited(resetsAt)`.
On detection, `WorkerDispatchService` raises the `DispatchPaused` integration event, which is broadcast as a `dispatch` system notification (`isActive: true`) so the dashboard usage-limit banner updates live without a page refresh.

## Dispatch Pause

A global operational state where the dispatch loop skips issuing new work.
Every runtime pause reason lives on one row of the Workers-owned `DispatchPause` aggregate (table `dispatch_pause`, fixed id `DispatchPauseId.Default`, created by `DispatchPauseSeeder` on a fresh database): two global triggers, `UsageLimitResetsAt` (automatic, from usage limit detection) and `IsDispatchPaused` (manual, from operator "Pause All" action), beside the [Host Pauses](#host-pause) (`HostPauses`, JSON column `host_pauses`) and the [Package Proxy Pause](#package-proxy-pause) (`PackageProxyPause`, nullable JSON column `package_proxy_pause`). Keeping them on one row means `ResumeAllDispatch` clears them all in one method, a pause can be staged unsaved and commit in the same save as the run failure that found its cause, and readers get them as one `DispatchPauseState` through `IDispatchPauseQueries` (`Workers.Contracts`). Workers serves `POST /api/settings/dispatch/pause` and `/dispatch/resume` on the Settings routes, answering with the settings summary.
Dispatch is paused when either is active.
Auto-resume clears `UsageLimitResetsAt` and retries all `FailureReason.UsageLimited` issues when the Global Settings preference `AutoResumeOnUsageReset` is enabled.
Manual "Resume All" clears both flags, every [Host Pause](#host-pause) and the [Package Proxy Pause](#package-proxy-pause), and retries usage-limited issues.

A third, independent pause is the **credit block**: when a run fails with `CreditsExhausted`, the `ClaudeAccount` aggregate's `SpendState` transitions to `Blocked(NextProbeAt)`, and `CredentialGate.CanDispatchAsync` then refuses dispatch regardless of the global flags. This pause carries no reset time — a money-based block cannot say when it clears — but it clears without an operator once the account can spend again (Team credits auto-reload, an admin raises the spend cap), so the block schedules a **Credit Probe** to poll for that moment (see Credit Probe). `SpendState` and `CredentialValidity` are independent: an account can be simultaneously credit-blocked and auth-invalid, the gate refuses on either, and clearing one leaves the other in force. Both a successful probe and a manual "Resume All" call `RestoreSpend()` and — only when the state actually changed — publish `CreditsRestored`, on which the Issues module re-queues every issue whose `FailureReason` equals `"Credits exhausted"` (an exact match, never a prefix sweep); the transition is idempotent so `CreditsRestored` publishes exactly once even when a probe and a manual resume converge.
Already-running workers are unaffected — only queued issues are held.
Both pause and resume broadcast a `dispatch` system notification (`isActive: true` on pause, `isActive: false` on resume); the client treats this as a pure reload trigger and re-syncs banner state from `/api/settings` (the authoritative source) rather than reading any timestamp off the SignalR payload.

## Host Pause

Dispatch and publishing held for one provider host (for example `gitlab.com`) because a run's [Publisher](#publisher) found it unreachable. Other hosts dispatch and publish as normal, so a gitlab.com outage never stops GitHub work. Queued issues for the paused host wait in the queue instead of each dispatching, failing and spending its own retry budget, and finished runs wait to publish instead of spending their publish attempts.
- **State:** `DispatchPause.HostPauses` (Workers), one immutable `HostPause(Host, PausedAt, ProbeAttempts, NextProbeAt)` per host, beside the other [Dispatch Pause](#dispatch-pause) reasons. A new pause has `ProbeAttempts = 0` and `NextProbeAt = PausedAt + 1 min`; each probe that does not clear it at `now` adds one attempt and sets `NextProbeAt = now + min(2^(ProbeAttempts+1) min, 10 min)`, so probes run 1, 2, 4 and 8 minutes apart, then every 10 minutes, without limit (`HostPause.AfterUnreachableProbe`).
- **Trip:** a Prepare that fails `ProviderUnreachable` calls `DispatchPause.PauseHost` with the repository's host (`MonitoredRepository.Host`, carried as `RepositoryDispatchInfo.Host`) in the same save as the run's failure (`IssueClaimedHandler`), and so does a publish attempt that fails `kind=unreachable` (`PublishResult.Failed` with `PublishFailureKind.Unreachable`, saved with the run's `PublishFailedRun`, `WorkerRunPublisher`). A host already paused is left as it is, so concurrent failures never reset its backoff. No other Prepare or publish failure trips it.
- **Gate:** `DispatchCandidateSelector` drops every repository whose host (`EligibleRepository.Host`) is in `DispatchPauseState.PausedHosts` before ordering candidates, which holds every queued kind (fresh, triage, revision, continuation). When every repository with queued work is on a paused host the selector returns `NoCandidates` and the slot is released through `ClaimSkipped`.
- **Publish wait:** the publishing loop (`RunPublishingService`) holds every run whose repository's host (`RepositoryDispatchInfo.Host`, matched case-insensitively against `DispatchPauseState.PausedHosts`) is paused: a `PublishFailedRun` whose retry is due is not retried and a `PublishingRun` is not published, so the run is left untouched and an outage spends none of its publish attempts. It retries or publishes on the first tick after the pause clears (by a probe or Resume All). The pause never extends the Hand-off time to live: a held `PublishFailedRun` or `PublishBlockedRun` past `DiscardsAt` is discarded as usual.
- **Probe:** `HostProbeService` (every 15 s) probes each paused host whose `NextProbeAt` has passed, never two probes of one host at once, and keeps probing during a global manual or usage-limit pause. `HostProber` tries up to 3 monitored repositories on the host, one Publisher Probe container after another, each with its token resolved as Prepare resolves it: most recent successful Prepare first (`WorkerRun.PreparedRepositoryId`, recorded with the run's base), repositories never prepared last. The first `reachable` calls `ClearHostPause`; when every probe is `unreachable` or inconclusive (container error, no `FOUNDRY_PROBE` line, a 2-minute timeout, or no repository with a usable token, each logged as a warning) it calls `RecordProbeUnreachable`. Probing through the Publisher, rather than from the WebApi process, uses the containers' own network path and the same `classify_error` as the trip, so the trip and the release can never disagree about what unreachable means.
- **Manual fallback:** "Resume All" clears every host pause (`ResumeAllDispatch`); the next dispatch to a still-unreachable host trips it again, with probing restarting at 1 minute.
- **Broadcast:** adding, advancing or clearing a host pause broadcasts a `dispatch` system notification once the change is saved, so the dashboard reloads its settings (Resume All's own `DispatchResumed` broadcast covers its clearing). Clearing a host never publishes `DispatchResumed`, whose consumer re-queues usage-limited failures.

## Package Proxy Pause

Dispatch held for every repository that declares [Package Sources](#package-source) because a worker could not reach the [Package Source Proxy](#package-source-proxy). The proxy is one endpoint for all repositories, so one failure says the same about all of them and there is one pause, not one per repository. Repositories without Package Sources dispatch as normal. Queued issues for affected repositories wait in the queue instead of each dispatching, failing `package_source_unreachable` and spending its own retry budget; recovery needs no person. Publishing is never held: it does not use the proxy.
- **State:** `DispatchPause.PackageProxyPause` (Workers), absent or one immutable `PackageProxyPause(PausedAt, ProbeAttempts, NextProbeAt)`, beside `HostPauses` and the other [Dispatch Pause](#dispatch-pause) reasons, persisted in the nullable JSON column `package_proxy_pause`. It shares the [Host Pause](#host-pause) backoff (`ProbeBackoff`): first probe at `PausedAt + 1 min`, then 2, 4 and 8 minutes apart, then every 10 minutes, without limit (`PackageProxyPause.AfterUnreachableProbe`). `DispatchPauseState.PackageProxyPause` carries it to the dispatcher; `GET /api/settings` returns it as `packageProxyPause` (`pausedAt`, `probeAttempts`, `nextProbeAt`, or `null`).
- **Trip:** a run that fails `PackageSourceUnreachable` calls `DispatchPause.PausePackageProxy` in the same save as the run's failure (`WorkerOutcomeApplier`, for both a failure with and without commits). A pause already present is left as it is, so concurrent failures never reset its backoff.
- **Gate:** `EligibleRepository.DeclaresPackageSources` is true when the repository's root config is `RootNuGetConfig.Declared` with at least one source (`MonitoredRepository.DeclaresPackageSources`), read at selection time, so a repository that gains or loses its sources while paused is gated by its current declaration. While the pause is present, `DispatchCandidateSelector` drops every such repository beside the paused-host filter, before ordering candidates. When every repository with queued work is held the selector returns `NoCandidates`.
- **Probe:** `PackageProxyProbeService` (every 15 s) probes once `NextProbeAt` has passed, never two probes at once, and keeps probing during a global manual or usage-limit pause. `PackageProxyProber` runs one Publisher **proxy-probe** container under the same hardening and limits as a host probe, with the worker's `host.docker.internal` extra-hosts entry and `PROXY_URL` set to `PackageSourceProxyOptions.WorkerBaseUrl` (through `IPackageSourceProxyAddress`), so it takes a worker's network path. It requests that URL once, unauthenticated, and prints `FOUNDRY_PROXY_PROBE result=reachable|unreachable detail=<one line>`: `reachable` when any HTTP status line came back (404 included), `unreachable` otherwise. `reachable` calls `ClearPackageProxyPause`; `unreachable`, or an inconclusive probe (container error, no `FOUNDRY_PROXY_PROBE` line, a 2-minute timeout, each logged as a warning), calls `RecordPackageProxyProbeUnreachable`.
- **Exit:** clearing the pause lets the held queued issues dispatch on the next tick. Issues already failed `package_source_unreachable` keep following that category's automatic retry schedule ([Transient Retry](#transient-retry)).
- **Manual fallback:** "Resume All" (`ResumeAllDispatch`) also clears this pause.
- **Broadcast:** adding, advancing or clearing the pause broadcasts the same `dispatch` system notification a host pause does, so the dashboard reloads its settings and shows or hides its banner.

## Credit Probe

The mechanism that auto-clears a credit block. Since the clearing moment is unknowable, Foundry polls for it with a cheap transient container rather than re-dispatching real work — a full resume would re-queue every stranded issue each cycle (N worker containers, N×2+ provider API calls, N `worker_runs` rows, N failed transitions, N broadcasts), while a probe reads the same single bit ("can this account spend right now?") with one short container and no issue state touched.

`SpendState.Blocked` carries a durable `NextProbeAt` — kept on the aggregate (not an in-memory timer) so the schedule survives a host restart. `CreditProbeService` (a `PeriodicBackgroundService` in the Credentials module, mirroring `TransientRetryService`) ticks ~30 s, and when `NextProbeAt` has passed it delegates to `CreditProbeCoordinator.TryRunProbeAsync`. The coordinator holds an in-process `SemaphoreSlim(1,1)` for single-flight — a second concurrent caller (a scheduled tick racing the operator's "Check now") is a no-op that reports the in-flight probe. It defers while `ILoginSessionState.IsLoginActive` (the same window `CredentialGate` refuses on) and logs-and-skips when no `ClaudeAccount` row exists.

The probe container mirrors the login/credential-helper containers: transient (`foundry.transient=true` so existing reaping applies and worker reaping never touches it), `foundry.role=credit-probe`, `AutoRemove`, built from the login image, running a trivial `claude -p` prompt under its own `timeout`, mounting the credential volume read-write at `/home/node/.claude` (OAuth; read-write like workers, though workers mount it only as their credential store — the CLI may refresh and rotate the token during the probe) or injecting `ANTHROPIC_API_KEY` (API key). Its captured logs are classified by `IProbeOutcomeClassifier` (a public surface in `Workers.Contracts` wrapping the internal `IContainerOutputParser`, so the money-vs-time-vs-infra discrimination has a single source of truth) into a `ProbeOutcome`:

- `Available` → `RestoreSpend()`; if changed, publish `CreditsRestored` (outbox) → Issues re-queues.
- `CreditsStillBlocked` (money-based 429, no reset time) → `RearmProbe(now + ProbeIntervalMinutes)`; no issue state changes.
- `UsageLimited(resetsAt)` (time-based 429, reset time parses) → publish `UsageLimitReached(resetsAt)` (outbox), which the Workers `UsageLimitReachedHandler` applies to `DispatchPause.UsageLimitResetsAt` (broadcasting `DispatchPaused` when the reset time changed), and clear the credit block (the money path is proven fine, so the usage-limit pause takes over) rather than blindly re-arming the probe.
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
All of these run through `UsagePollCoordinator`, which holds a `SemaphoreSlim(1,1)` for single-flight but, unlike the Credit Probe's fail-fast coordinator, has a second concurrent caller **join** the in-flight poll rather than being dropped, so an event-triggered poll arriving during a scheduled tick still refreshes without starting a second container.

## System Notification

A lightweight SignalR broadcast (`Category`, `IsActive`, `Message`) delivered to all dashboard clients via the system hub, used for global operational banners.
Categories in use: `dispatch`, `docker`, `image-build`, `credits`, `account-token`, `account-token-expiry`.
The `account-token` category (see Invalid Token) carries a server-built message counting accounts whose token is Invalid — `isActive: true` while the count is above zero, `isActive: false` once every account token is valid again — and `/api/system/status` seeds it on page load.
The `account-token-expiry` category carries a server-built warning counting Valid accounts whose Token Expiry is expiring soon (within 14 days) or has passed — Invalid accounts are left to `account-token`, so the two categories show independently. It is recounted hourly (broadcast only when the counts change) and on every account change (token set, Invalid/Valid transition, deletion), and `/api/system/status` seeds it on page load.
Auth-invalid and auth-restored conditions broadcast on `dispatch` (they gate dispatch via `ClaudeAccount.Validity`), not on a separate category.
Client semantics: an `isActive: true` notification is stored, replacing any prior entry for its category; `isActive: false` removes the category's entry.
Both `dispatch` and `image-build` are pure reload triggers — each broadcast carries an empty message and signals the client to re-sync authoritative state from `/api/settings` rather than reading any payload (see Dispatch Pause and Worker Image Build State).
The `credits` category (see Credit Probe) is its own reload trigger — `isActive: true` on credit block or a probe re-arm, `isActive: false` on restore — re-syncing the countdown from `/api/credentials`. It is deliberately independent of `dispatch`: an auth-invalid condition and a credit block are independent and can hold at once, so each holds its own category and both banners render simultaneously rather than one replacing the other.
Ephemeral — broadcast directly, never through the transactional outbox, since there is no durable consumer.

## Container Output Parser

An infrastructure service (`IContainerOutputParser`) that classifies a worker container's JSON output.
Takes the captured `--output-format stream-json` output, selects the last `type: "result"` event (see Worker Outcome Detection), and returns a discriminated result: `NormalExit`, `UsageLimited(DateTimeOffset ResetsAt)`, `CreditsExhausted`, `AuthInvalid`, `TransientApiError`, `ParseFailure(string RawOutput)`, `NoResultLine`, `AgentStartedNoResultLine`, or `WorkerBootstrapFailed(string Detail)`.
Inspects `ResultMessage.api_error_status` (429) as the primary limit signal, with the `terminal_reason` allowlist as a secondary signal. When a limit signal is present, reset-time parse **success** is the discriminator (see ADR 0014): if a reset time is extracted from the result text via best-effort regex (wall-clock or ISO-8601) the result is `UsageLimited(resetsAt)`; if none parses the result is `CreditsExhausted`. `Parse` takes no cooldown fallback parameter — the former fabricated `DefaultCooldownMinutes` reset time is removed.
Auth failures are detected from `api_error_status == 401`, with `error.type == "authentication_error"` as a secondary guard.
Transient API faults are detected from `api_error_status` in the 5xx range (the durable signal), or — when `api_error_status` is null and `is_error: true` — from a narrow frozen allow-list of transient `result` phrases (`"API Error: Connection closed mid-response"`, `"API Error: 529 Overloaded"`) matched case-sensitively as a substring. The predicate is status-5xx-or-allow-list, never a bare `is_error: true`, so genuine task failures are never laundered into a retryable category. When `is_error: true` matches no known category (usage-limit, auth, transient), the parser logs a warning naming the unclassified `result` text — a tripwire for a reworded transient phrase that the brittle allow-list would otherwise miss silently.
Bootstrap failures are detected via a sentinel line (`FOUNDRY_BOOTSTRAP_FAILED stage=… detail=…`) emitted by `entrypoint.sh` when the container dies before `claude` runs (clone, auth, branch, managed-settings, role, package-sources or package-restore stage — the last two parse to `PackageSourceUnreachable` and `PackageRestoreFailed` instead of `WorkerBootstrapFailed`; the [Publisher](#publisher)'s Prepare also prints it, with stage `clone` or `unreachable`); the parser scans for the sentinel only when no Claude JSON result line is present, so a genuine result always wins. When there is no result line but at least one line parses as a stream-json event (a JSON object with a string `type`), the agent demonstrably started — bootstrap output goes to stderr and `claude` is the last thing the entrypoint runs — so the parser returns `AgentStartedNoResultLine` and never `WorkerBootstrapFailed`, whatever sentinel text the output (e.g. a tool result) contains; the resolver maps it to `NonZeroExit(exitCode)` after the external-kill checks. Otherwise the sentinel counts only at the start of a line (after the optional Docker timestamp), so a mid-line quote is ignored, and when the 64 KB tail cut lands mid-line the partial first line is dropped before matching. A non-zero exit with no result line, no stream-json event and no sentinel falls back to `WorkerBootstrapFailed` heuristically.
Domain types remain JSON-unaware — all parsing is in infrastructure.
`IContainerOutputParser` and `ContainerOutputParseResult` are `internal` to the Workers module; the Credentials credit probe (which runs outside Workers) reaches the same classification through `IProbeOutcomeClassifier`/`ProbeOutcome` in `Workers.Contracts`, a public wrapper that maps each parse result to the coarser probe outcome (`Available`, `CreditsStillBlocked`, `UsageLimited`, `InfrastructureFailure`) so there is one source of truth for the money-vs-time-vs-infrastructure discrimination.
