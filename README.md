# Foundry

A containerized service that monitors repositories across multiple providers (GitHub, GitLab) for issues tagged with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them.

## Prerequisites

- Docker Desktop (running)
- .NET 10 SDK
- Node.js 22+

## One-time setup

```bash
dotnet user-secrets init --project src/Foundry.WebApi
dotnet user-secrets set "Workers:ApiKey" "" --project src/Foundry.WebApi
dotnet user-secrets set "Monitoring:Secrets:github-default" "" --project src/Foundry.WebApi
dotnet user-secrets set "Workers:ConfigPath" "" --project src/Foundry.WebApi
```

Fill in actual values for your Anthropic API key, GitHub PAT, and path to your `.claude` directory.

## Running

```bash
dotnet run --project src/Foundry.AppHost
```

This command:

- Builds the worker Docker image via Aspire's `AddDockerfile` (cached after first run)
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
