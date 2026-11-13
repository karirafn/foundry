# Foundry

A containerized service that monitors repositories across multiple providers (GitHub, GitLab) for issues tagged with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them.

## Prerequisites

- Docker Desktop (running)
- .NET 10 SDK
- Node.js 22 LTS (use [fnm](https://github.com/Schniz/fnm) — `.node-version` pins the version)

## One-time setup

All operator configuration — provider accounts and their tokens, monitored repositories, the Claude credential (API key or OAuth login), and every worker setting — is entered in the dashboard's **Settings** pages and stored in the database. There are no configuration keys or user secrets to set (the one exception is the migration-only `DataProtection:LegacyApplicationName` bootstrap key, see [Moving an install](#moving-an-install)); the database is the only source for worker settings ([ADR 0080](docs/adr/0080-all-worker-settings-in-global-settings.md)).

### GitHub fine-grained token permissions

Fine-grained personal access tokens are preferred over classic PATs — they offer a smaller scope and more precise permission control.

Generate a fine-grained token at **Settings > Developer settings > Personal access tokens > Fine-grained tokens** and grant the following repository permissions:

| Permission | Access |
|---|---|
| Contents | Read and write |
| Metadata | Read |
| Pull requests | Read and write (optional — required only if workers need to create or update PRs) |

This is the **account (worker) token**. It keeps exactly this permission set — do not add Administration to it. It is injected into every worker container (as `GH_TOKEN`), so any permission it carries is available to autonomous workers.

#### Branch-rules token (optional)

An account can also carry a second, optional **branch-rules token** (the account's *server-side token*), entered in the account form's *Branch-rules token* field. Foundry uses it server-side only, for privileged provider writes — default-branch protection and repository webhooks; it is stored encrypted like the account token, is never returned by the API (only whether it is configured, absent or unreadable), and is never given to workers.

| Provider | Required permission |
|---|---|
| GitHub (fine-grained token) | Administration — Read and write (branch rules); Webhooks — Read and write (webhooks) |
| GitLab | Maintainer role on the project (both) |

It is kept separate from the account token because GitHub's Administration: write also grants repository deletion and rule removal, Webhooks: write lets the holder re-point or add hooks (and so read every event), and GitLab's Maintainer role can likewise change protection and hooks — power autonomous workers must never hold. The worker token's required permissions are unchanged. The two tokens may belong to different users. Leave the field empty if you don't want Foundry to manage branch protection; rotating the account token keeps the branch-rules token unless you change it.

### Worker environment

Worker container resource limits (memory, CPU, PIDs) are managed on the **Worker environment** settings page in the dashboard, stored in the database, and read fresh on each dispatch. Workers take no configuration from the operator's host: Foundry does not bind-mount host files or directories into worker containers and does not import hooks from a host Claude Code `settings.json`.

The `Workers` configuration section (limits, model, deny rules) is no longer read; re-enter any values you had there in the dashboard (there is no automatic import, per ADR 0080).

### Worker settings

Foundry generates a `settings.json` for each worker container at dispatch time and injects it via the `CLAUDE_SETTINGS_JSON` environment variable.
The entrypoint writes it to `/home/node/.claude/settings.json` before Claude Code starts.

**Base deny list** — always enforced, not configurable:

- `Bash(git push --force:*)`
- `Bash(git push * main)`
- `Bash(git push * master)`
- `Bash(npm publish:*)`
- `Bash(npx -y:*)`
- `Bash(git branch -D:*)`
- `Bash(git branch -d:*)`
- `Bash(git push --delete:*)`
- `Bash(git push * HEAD:*)`
- `Bash(git push * :*)`
- `Bash(git merge:*)` and `Bash(git pull:*)` — a worker catches its branch up with the default branch only by `git rebase origin/HEAD` (ADR 0101)

The additional deny rules are managed on the Claude Code settings page in the dashboard and stored in Global Settings. The model is set per Worker Role in Settings → Worker roles, chosen from Default (the account default), the Claude Code aliases and the models the Anthropic Models API lists for the workers' credential.

> **Security model note:** worker prompts ship in the worker image with each Worker Role (`workers/roles/<role>/`, shown in Settings → Worker roles, where only the model can be changed), so a prompt change is a reviewed pull request that takes effect at the next restart. Anyone who can merge to the repository is trusted — they can weaken or override guidance in a role prompt. The non-configurable safety preamble (prepended automatically) provides a baseline, but it is instructional guidance, not a cryptographic enforcement mechanism. Do not rely on it alone to contain an adversarial operator.

## Running

```bash
dotnet run --project src/Foundry.AppHost
```

This command:

- Starts the WebApi with auto-migration applied on startup
- Starts the Angular dashboard

The worker image tag and build toggle live in **Global Settings** (database), configured via the Worker Image settings page.
With building enabled (the default), the WebApi builds the worker Docker image at startup; the first run is slower due to the Docker image build, and subsequent runs use the Docker layer cache.
With building disabled, Foundry inspects the configured tag on the Docker host at startup — if the image exists, the build state becomes `Idle`; if it is absent, the state becomes `Failed` and dispatch is blocked until the image is made available and the operator retries.

## Webhooks (optional)

By default Foundry polls every repository. GitHub and GitLab webhooks make it react within seconds and let repositories with a healthy webhook drop to a slower **backstop** poll (15 minutes by default). Webhook deliveries only *nudge* the existing poll passes — they never change state directly (ADR 0085).

1. **Enable the webhook port.** Set `Webhooks:Port` in the AppHost configuration, for example with user secrets:

   ```bash
   dotnet user-secrets --project src/Foundry.AppHost set "Webhooks:Port" "5099"
   ```

   The WebApi then binds a second, plain-HTTP endpoint on that port that serves **only** `/webhooks/*` — every other path returns 404 there, and `/webhooks/*` returns 404 on the main port. Without the setting no webhook endpoint exists.

2. **Expose that port publicly** through a tunnel or reverse proxy that terminates TLS, and forward only that port. The WebApi itself is unauthenticated, so never expose the main port. For example:

   ```bash
   # Cloudflare Tunnel
   cloudflared tunnel --url http://localhost:5099
   # ngrok
   ngrok http 5099
   ```

   With a reverse proxy (Caddy, nginx, Traefik), route `https://hooks.example.com/webhooks/*` to `http://<foundry-host>:5099`.

3. **Set the public base URL.** In **Settings → Dispatch → Webhooks**, enter the public `https` URL the tunnel or proxy serves (e.g. `https://hooks.example.com`) and adjust the backstop interval if needed.

4. **Register the webhook per repository.** If the repository's account has a [branch-rules token](#branch-rules-token-optional) with the webhook permission above, Foundry registers the webhook itself: when the repository is added, when that token is saved (for the account's not-yet-registered repositories), when the public base URL changes (every registered hook is re-pointed), and once whenever a hook turns **Silent**. It creates the hook — or updates a hook with the same payload URL in place, adopting one you added by hand — with the payload URL, the secret, JSON content type and exactly the events below, and the status becomes *Waiting for first delivery*. The repository's Webhook panel also has a **Register webhook** button that runs the same create-or-update. Without the token, or when the token lacks the permission (GitHub answers 403/404 on `/hooks`, GitLab 403 below Maintainer), nothing changes and you register the hook manually as below; polling is never blocked.

   A hook still pointing at an *old* public base URL that Foundry did not re-point (for example one added by hand before the URL changed) is not matched, so a second hook is created; delete the old one on the provider — it only fails harmlessly. Removing a repository from Foundry does not delete its hook.

   Whether GitHub additionally requires repository admin rights beyond the fine-grained **Webhooks** permission has not been verified with a real token; the GitHub REST documentation lists only *Webhooks* (write) for these endpoints. If it does, registration reports insufficient permission and the manual steps below remain the fallback.

   To register manually, open the repository in **Settings → Repositories**. The Webhook panel shows the payload URL (`<public base URL>/webhooks/github/<hookId>`), the content type (`application/json`), the secret (revealed on demand) and the exact events to select. Add the webhook in the repository's GitHub settings, then click **I've added the webhook**. The status shows *Waiting for first delivery* until GitHub's `ping` (or any event) arrives — redeliver the `ping` from the webhook's *Recent Deliveries* tab if needed.

   For a **GitLab** repository the payload URL is `<public base URL>/webhooks/gitlab/<hookId>`. Paste the revealed secret as the hook's **Secret token** and tick the listed triggers (Issues/Work item events, Comments, Merge request events, Pipeline events, Push events, plus the confidential variants if you track confidential issues). GitLab has no `ping`, so after clicking **I've added the webhook** use the hook's **Test** action (for example *Issues events*) to send the first delivery. GitLab has no event for issue links or protected branches; while the hook is active those changes are picked up by the backstop poll.

A repository polls at the backstop interval only while its webhook is **Active**. While it is active, a running worker's commit count is refreshed from push deliveries instead of a branch compare every 10 seconds. A hook registered before the `push` / *Push events* subscription was added should have it ticked, or the count only updates when the worker exits. If a scheduled poll finds a change that no delivery announced, the status becomes **Silent** and normal polling resumes until the next delivery.

## Moving an install

Stored secrets (account tokens, branch-rules tokens, webhook secrets, the Claude API key) are encrypted with ASP.NET Core Data Protection under the fixed application name `Foundry`, so they survive moving the install to another folder or operating system. To move an install, copy both of these to the same relative places in the new checkout:

- the database directory `data/` at the repository root (the AppHost default, `../../data` relative to `src/Foundry.AppHost`);
- the key ring `src/Foundry.WebApi/data/dp-keys` (relative to the WebApi working directory, which is `src/Foundry.WebApi` under the AppHost).

An in-place upgrade needs nothing further.

If the old install ran a version from before the application name was pinned, from a different path than the new one, its values are still bound to the old content root. Set `DataProtection:LegacyApplicationName` to the old WebApi content root, *including* its trailing separator (for example `C:\dev\foundry\src\Foundry.WebApi\`), for one start. At startup Foundry re-encrypts every value that only decrypts under that name, in a single transaction; afterwards remove the key.

The AppHost does not forward this key, so set it on the WebApi directly, with either:

- an environment variable in the shell that starts the AppHost (child processes inherit it): `DataProtection__LegacyApplicationName`;
- WebApi user secrets (loaded in the Development environment): `dotnet user-secrets --project src/Foundry.WebApi set "DataProtection:LegacyApplicationName" "C:\dev\foundry\src\Foundry.WebApi\"`.

A value that decrypts under neither name is left unchanged and logged as a warning naming its table, column and row id (`... decrypts under neither the current nor the legacy Data Protection discriminator and was left unchanged`). Re-enter those values in the dashboard.

## Development

```bash
# Build
dotnet build

# Test
dotnet test

# Run single test
dotnet test --project tests/Foundry.UnitTests --filter-class "*ExampleTests"

# Frontend
cd src/foundry-web
npm install
npx ng serve
```

See [CLAUDE.md](CLAUDE.md) for full architecture documentation.
