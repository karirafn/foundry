# Foundry

A containerized service that monitors repositories across multiple providers (GitHub, GitLab) for issues tagged with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them.

## Prerequisites

- Docker Desktop (running)
- .NET 10 SDK
- Node.js 22 LTS (use [fnm](https://github.com/Schniz/fnm) — `.node-version` pins the version)

## One-time setup

```bash
dotnet user-secrets init --project src/Foundry.WebApi
dotnet user-secrets set "Monitoring:Secrets:github-default" "<your-github-pat>" --project src/Foundry.WebApi
dotnet user-secrets set "Monitoring:Repositories:0:Slug" "owner/repo" --project src/Foundry.WebApi
```

Set exactly one worker credential — the application rejects both or neither at startup.

- **Pay-per-use API:** `dotnet user-secrets set "Workers:ApiKey" "<your-anthropic-api-key>" --project src/Foundry.WebApi`
- **Max plan (OAuth):** `dotnet user-secrets set "Workers:OAuthToken" "<token-from-claude-setup-token>" --project src/Foundry.WebApi`

### GitHub fine-grained token permissions

Fine-grained personal access tokens are preferred over classic PATs — they offer a smaller scope and more precise permission control.

Generate a fine-grained token at **Settings > Developer settings > Personal access tokens > Fine-grained tokens** and grant the following repository permissions:

| Permission | Access |
|---|---|
| Contents | Read and write |
| Metadata | Read |
| Pull requests | Read and write (optional — required only if workers need to create or update PRs) |

### Worker bind mounts

Worker host bind mounts and container resource limits (memory, CPU, PIDs) are managed on the **Worker environment** settings page in the dashboard, stored in the database, and read fresh on each dispatch. Each mount is keyed on its container path with a read-only/writable flag. Do not mount `settings.json` directly — it is generated at dispatch time from `Workers:Settings` (see below).

Legacy `Workers:Mounts`, `Workers:WritableMounts`, `Workers:MemoryLimitMb`, `Workers:CpuLimit` and `Workers:PidsLimit` config keys are no longer read after upgrade; re-enter them on the Worker environment settings page (there is no automatic import, per ADR 0080).

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

**Hooks** — edit `secrets.json` directly for the complex hook structure (array values are not supported by the `dotnet user-secrets set` command):

```bash
dotnet user-secrets edit --project src/Foundry.WebApi
```

```json
{
  "Workers": {
    "Settings": {
      "Hooks": {
        "PreToolUse": [
          {
            "Matcher": "Bash",
            "Hooks": [
              { "Type": "command", "Command": "/hooks/my-hook.sh" }
            ]
          }
        ]
      }
    }
  }
}
```

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
