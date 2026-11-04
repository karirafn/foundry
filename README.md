# Foundry

A containerized service that monitors repositories across multiple providers (GitHub, GitLab) for issues tagged with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them.

## Prerequisites

- Docker Desktop (running)
- .NET 10 SDK
- Node.js 22 LTS (use [fnm](https://github.com/Schniz/fnm) — `.node-version` pins the version)

## One-time setup

All operator configuration — provider accounts and their tokens, monitored repositories, the Claude credential (API key or OAuth login), and every worker setting — is entered in the dashboard's **Settings** pages and stored in the database. There are no configuration keys or user secrets to set; the database is the only source for worker settings.

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

An account can also carry a second, optional **branch-rules token**, entered in the account form's *Branch-rules token* field. Foundry uses it server-side only, to write default-branch protection; it is stored encrypted like the account token, is never returned by the API (only whether it is configured, absent or unreadable), and is never given to workers.

| Provider | Required permission |
|---|---|
| GitHub (fine-grained token) | Administration — Read and write |
| GitLab | Maintainer role on the project |

It is kept separate from the account token because GitHub's Administration: write also grants repository deletion and rule removal, and GitLab's Maintainer role can likewise change protection — power autonomous workers must never hold. The two tokens may belong to different users. Leave the field empty if you don't want Foundry to manage branch protection; rotating the account token keeps the branch-rules token unless you change it.

### Worker bind mounts

Worker host bind mounts and container resource limits (memory, CPU, PIDs) are managed on the **Worker environment** settings page in the dashboard, stored in the database, and read fresh on each dispatch. Each mount is keyed on its container path with a read-only/writable flag. Do not mount `settings.json` directly — it is generated at dispatch time from Global Settings (model and deny rules), with hooks merged in from the host settings file (see Worker settings below).

The `Workers` configuration section (mounts, limits, hooks, model, deny rules) is no longer read; re-enter any values you had there in the dashboard (there is no automatic import, per ADR 0080).

On Windows, use the full Windows path (e.g. `C:\Users\you\.claude\skills`). Docker Desktop must have path sharing enabled — see Windows notes below.

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

The Claude Code model and additional deny rules, including the CI/CD edit protection toggle, are managed on the Claude Code settings page in the dashboard and stored in Global Settings.

> **Security model note:** the system prompt template is an operator-controlled setting, edited in the dashboard (Settings → Prompts) and stored in Global Settings. Operators are trusted — a malicious operator can weaken or override guidance in the system prompt template. The non-configurable safety preamble (prepended automatically) provides a baseline, but it is instructional guidance, not a cryptographic enforcement mechanism. Do not rely on it alone to contain an adversarial operator.

**Hooks** — sourced from your host Claude Code `settings.json`. Set its path (and any hook commands to exclude, such as host-specific checks) on the **Worker environment** settings page. Each container start merges the file's `hooks` into the worker settings and logs `Host hooks applied: N (excluded: M)`; a configured file that is missing or not valid JSON fails the worker at bootstrap. Hook commands that reference `~/.claude/hooks/...` resolve through your existing bind mounts.

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

4. **Register the webhook per repository.** Open the repository in **Settings → Repositories**. The Webhook panel shows the payload URL (`<public base URL>/webhooks/github/<hookId>`), the content type (`application/json`), the secret (revealed on demand) and the exact events to select. Add the webhook in the repository's GitHub settings, then click **I've added the webhook**. The status shows *Waiting for first delivery* until GitHub's `ping` (or any event) arrives — redeliver the `ping` from the webhook's *Recent Deliveries* tab if needed.

   For a **GitLab** repository the payload URL is `<public base URL>/webhooks/gitlab/<hookId>`. Paste the revealed secret as the hook's **Secret token** and tick the listed triggers (Issues/Work item events, Comments, Merge request events, Pipeline events, plus the confidential variants if you track confidential issues). GitLab has no `ping`, so after clicking **I've added the webhook** use the hook's **Test** action (for example *Issues events*) to send the first delivery. GitLab has no event for issue links or protected branches; while the hook is active those changes are picked up by the backstop poll.

A repository polls at the backstop interval only while its webhook is **Active**. If a scheduled poll finds a change that no delivery announced, the status becomes **Silent** and normal polling resumes until the next delivery.

## Windows notes

Docker Desktop must have path sharing enabled for bind mounts from `C:\Users\...`. Enable it under **Settings > Resources > File sharing**.

## Development

```bash
# Build
dotnet build

# Test
dotnet test

# Run single test
dotnet test --filter "FullyQualifiedName~ExampleTests.ExampleTest"

# Frontend
cd src/foundry-web
npm install
npx ng serve
```

See [CLAUDE.md](CLAUDE.md) for full architecture documentation.
