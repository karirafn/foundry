# Workers Restore Private NuGet Package Sources Through a Foundry Proxy

> **2026-11-18 — Overridden in part:** the Decision clause "Workers reach it through an explicit `host.docker.internal:host-gateway` entry" no longer holds. The claim was false from the start for Docker Desktop with Foundry running in a WSL2 distro: Docker Desktop resolves `host-gateway` to its own address for the Windows host (`192.168.65.254`, plus an IPv6 `fdc4:…::254`), not to the distro where the WebApi listens, so every restore failed with NU1301 (#1122). The entry is now chosen on each dispatch: when the engine reports its operating system as `Docker Desktop` and Foundry runs in WSL (`/proc/sys/kernel/osrelease` contains `microsoft`), `host.docker.internal` maps to the IPv4 address of the interface carrying Foundry's host's default route; in every other setup, and when no such address is found (with a warning), it stays `host-gateway`. The proxy's host name, and so the generated `nuget.config`, is unchanged.

## Context

Repositories that pull packages from a private NuGet package source declare it in `nuget.config` with credentials taken from environment variables (`%PACKAGE_REGISTRY_USERNAME%`, `%NUGET_FEED_TOKEN%`, …), which their CI supplies.
Workers hold no provider credential for any role ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)), so those variables are unset and every restore against the source fails with `401 (Unauthorized)`, taking build, unit tests, integration tests and the C# LSP pre-restore down with it.
As of 2026-11-15 every captured pre-restore in two monitored GitLab repositories that use a private group package source failed this way; that source is on gitlab.com, the same host as the git provider.
GitLab rejects anonymous requests to group endpoints, even for public groups (GitLab docs, *NuGet packages in the package registry* § "Use the GitLab endpoint for NuGet packages"), so some credential is always required.

## Decision

Foundry holds every package source credential and serves private Package Sources to workers through the **Package Source Proxy**, an authenticating read-only proxy; no package credential enters a worker.

- **Package Source** — a remote source declared under `<packageSources>` in the repository's root `nuget.config`, other than nuget.org.
- **Package Source Proxy** — a Kestrel port on the WebApi that serves only proxied Package Sources, separated from the main API by the same local-port guard as the webhook port ([ADR 0085](0085-webhooks-nudge-the-poll-loop.md)). It forwards only `GET`/`HEAD` upstream with Foundry's credential and rewrites the upstream base URL in response bodies to its own, because NuGet v3 service indexes and registrations carry absolute URLs. Workers reach it through an explicit `host.docker.internal:host-gateway` entry.
- **Run Source Token** — Foundry mints a random token at dispatch and stores only its hash on the run. The worker presents it as Basic credentials; the proxy accepts it only while the run is live and only for Package Sources the run's repository declares. It is not a provider credential.
- **Redirect without editing the repository** — Foundry generates a `nuget.config` from the repository's root config (Package Sources re-pointed at the proxy; keys, source mapping and other sections kept), and the entrypoint exports `RestoreConfigFile` pointing at it. MSBuild reads environment variables as properties, so restore, build, test and `dotnet add package` use it while the tracked `nuget.config` stays untouched.
- **Credential resolution** — a **Package Source Credential** registered in Settings for the source's service-index URL, else the account credential that covers the repository's namespace, sent only to a source on that account's host.
- **Eligibility gate** — every Package Source is probed through the proxy's upstream path; one Foundry cannot read makes the repository Ineligible (`package-source-unreachable:<url>`), and the next successful probe clears it.

## Considered Options

- **A read-only package credential in the worker environment, filling the repository's `%VAR%` placeholders** — rejected: it reverses ADR 0100 for package credentials, and because the package source host is the provider host it makes blocking provider egress from workers (#61) impossible.
- **The Publisher pre-restores into a packages cache on the Hand-off volume** — rejected: restore evaluates repository-controlled MSBuild with the token present, which breaks ADR 0100's rule that the token holder never runs agent-written state; the Publisher would need the .NET SDK; and the agent could not add a private package mid-run.
- **Reading the credential from GitLab group CI/CD variables** — rejected: reading them needs the group's Owner role, and a token with that role can already read the group's package source, so the account-credential fallback covers the same cases without a second source.
- **Rewriting the workspace `nuget.config` and hiding it with `git update-index --skip-worktree`** — rejected: it would also cover `dotnet tool restore`, but it hides the agent's own edits to that file from git and silently drops them from the Run bundle.

## Consequences

- `dotnet tool restore` does not honour `RestoreConfigFile` and still reads the repository's own config, so a tool manifest restored through a private Package Source fails in the worker.
- Inside a worker, restore follows the default branch's root `nuget.config`; an agent's edit to that file on its branch has no effect until it merges, though CI honours it.
- A Package Source declared only in a nested `nuget.config` is not detected or proxied.
