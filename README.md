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

### Worker bind mounts

Workers run in sandboxed Docker containers. Use `Mounts` (read-only) and `WritableMounts` (read-write) to share host paths into every worker container.

Dictionary keys are container paths; values are host paths. Symlinks are resolved at dispatch time.

Common mounts for Claude Code workers:

| Host path | Container path | Dictionary |
|---|---|---|
| `~/.claude/skills` | `/root/.claude/skills` | `Mounts` |
| `~/.claude/rules` | `/root/.claude/rules` | `Mounts` |
| `~/.claude/commands` | `/root/.claude/commands` | `Mounts` |
| `~/.claude/hooks` | `/root/.claude/hooks` | `Mounts` |
| `~/.claude/settings.json` | `/root/.claude/settings.json` | `Mounts` |
| `~/.claude/plugins` | `/root/.claude/plugins` | `Mounts` |
| `~/.claude/observations` | `/root/.claude/observations` | `WritableMounts` |

Example — mount skills and rules read-only, observations read-write:

```bash
dotnet user-secrets set "Workers:Mounts:/root/.claude/skills" "/home/user/.claude/skills" --project src/Foundry.WebApi
dotnet user-secrets set "Workers:Mounts:/root/.claude/rules" "/home/user/.claude/rules" --project src/Foundry.WebApi
dotnet user-secrets set "Workers:WritableMounts:/root/.claude/observations" "/home/user/.claude/observations" --project src/Foundry.WebApi
```

On Windows, use the full Windows path (e.g. `C:\Users\you\.claude\skills`). Docker Desktop must have path sharing enabled — see Windows notes below.

## Running

```bash
dotnet run --project src/Foundry.AppHost
```

This command:

- Builds the worker Docker image via MSBuild target (cached after first run)
- Starts the WebApi with auto-migration applied on startup
- Starts the Angular dashboard

The first run is slower due to the Docker image build. Subsequent runs use the Docker layer cache.

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
