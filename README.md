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

Workers run in sandboxed Docker containers. Use `Mounts` (read-only) and `WritableMounts` (read-write) to share host paths into every worker container.

Dictionary keys are container paths; values are host paths. Symlinks are resolved at dispatch time.

Common mounts for Claude Code workers:

| Host path | Container path | Dictionary |
|---|---|---|
| `~/.claude/skills` | `/home/node/.claude/skills` | `Mounts` |
| `~/.claude/rules` | `/home/node/.claude/rules` | `Mounts` |
| `~/.claude/commands` | `/home/node/.claude/commands` | `Mounts` |
| `~/.claude/hooks` | `/home/node/.claude/hooks` | `Mounts` |
| `~/.claude/plugins` | `/home/node/.claude/plugins` | `Mounts` |
| `~/.claude/observations` | `/home/node/.claude/observations` | `WritableMounts` |

Do not mount `settings.json` directly — it is generated at dispatch time from `Workers:Settings` (see below).

Example — mount skills and rules read-only, observations read-write:

```bash
dotnet user-secrets set "Workers:Mounts:/home/node/.claude/skills" "/home/user/.claude/skills" --project src/Foundry.WebApi
dotnet user-secrets set "Workers:Mounts:/home/node/.claude/rules" "/home/user/.claude/rules" --project src/Foundry.WebApi
dotnet user-secrets set "Workers:WritableMounts:/home/node/.claude/observations" "/home/user/.claude/observations" --project src/Foundry.WebApi
```

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

**Model** — omit to let Claude Code use its own default; set to pin a specific model:

```bash
dotnet user-secrets set "Workers:Settings:Model" "claude-sonnet-4-5" --project src/Foundry.WebApi
```

**Additional deny rules** — appended after the base deny list:

```bash
dotnet user-secrets set "Workers:Settings:AdditionalDenyRules:0" "Bash(rm -rf:*)" --project src/Foundry.WebApi
```

Rules must follow the `Tool(pattern:*)` format. For Bash rules, use `Bash(command:*)` — the `:*` suffix enables glob argument matching. A rule without the colon (e.g. `Bash(curl)`) matches only that exact string.

**CI/CD deny defaults** — a configurable default deny list that blocks edits to CI/CD files.
Unlike the base deny list, operators can clear it by binding an empty array:

- `Edit(.github/workflows/**:*)`
- `Edit(.gitlab-ci.yml:*)`
- `Edit(Dockerfile:*)`
- `Edit(docker-compose*.yml:*)`
- `Edit(docker-compose.yaml:*)`
- `Edit(compose.yml:*)`
- `Edit(compose.yaml:*)`

To clear the CI/CD deny defaults, edit `secrets.json` directly:

```bash
dotnet user-secrets edit --project src/Foundry.WebApi
```

```json
{
  "Workers": {
    "Settings": {
      "CiCdDenyRules": []
    }
  }
}
```

Note: the system prompt still instructs workers not to touch CI/CD files even when this list is cleared (soft guidance).

> **Security model note:** `Workers:SystemPromptTemplate` is operator-controlled configuration. Operators are trusted — a malicious operator can weaken or override guidance in the system prompt template. The non-configurable safety preamble (prepended automatically) provides a baseline, but it is instructional guidance, not a cryptographic enforcement mechanism. Do not rely on it alone to contain an adversarial operator.

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

- Starts the WebApi, which builds the worker Docker image at startup (controlled by `Workers:ImageBuild:Enabled`)
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
