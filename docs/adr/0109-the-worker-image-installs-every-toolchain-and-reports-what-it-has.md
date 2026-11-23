---
reaches: structure
---

# The Worker Image Installs Every Toolchain and Reports What It Has

## Context

The optional toolchains (`INSTALL_DOTNET`, `INSTALL_ANGULAR`, `INSTALL_CHROMIUM`, `INSTALL_DOCKER`) were operator flags on `GlobalSettings`.
In practice every flag stays on, and the flags do two more jobs than shaping the image.
The C# LSP prompt section reads `GetWorkerImageInstallsDotnetAsync`.
The rootless Docker allowances of [ADR 0079](0079-relaxed-allowances-for-nested-rootless-docker.md) read `GetWorkerImageInstallsDockerAsync`.
A flag can disagree with the image a run actually gets: building can be disabled, or the image can come from an earlier build.
The Docker flag also grants `SYS_ADMIN`, `DAC_OVERRIDE` and unconfined seccomp and AppArmor to `Restricted` runs ([ADR 0098](0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md)), and the entrypoint starts `dockerd` for them, although a restricted run has no tool that can use a daemon.

This replaces those toolchain flags, the clause that the 0079 allowances follow the `InstallDocker` flag, and 0079's settings-toggle sentence.

## Decision

The worker image installs every toolchain unconditionally, and the four flags are removed from `WorkerImageConfiguration`.
The image tag, the build toggle, the rebuild operation, the build state and the dispatch gate stay.

What an image contains is read from the image, not from settings.
One script in the image prints its toolchain list (name and version).
After every successful build, and on inspect when building is disabled, Foundry runs that script in the new image and stores the list per image ID.
Settings shows it read-only.
The entrypoint runs the same script at start and reports one `FOUNDRY_TOOLCHAIN` line, which Foundry stores on the run, so a run's record is the container it actually ran in.

Prompt sections that depend on a toolchain (C# LSP, Playwright) are included from the stored list.
An image without a list gets none of them.
Foundry retries the probe with backoff at startup and on the image sweep until the list is recorded, so a failed probe never waits for the next build to clear.

Rootless Docker follows the run's capability profile, not the image.
A `Full` run gets the 0079 allowances and a `dockerd` only while the operator's nested-Docker switch is on ([ADR 0110](0110-nested-docker-privileges-stay-an-operator-switch.md)); `Restricted` runs get neither.

## Considered Options

- **Keep the flags as optional toggles**: rejected, because no toolchain is ever turned off and each flag is a second source of truth for what the image holds.
- **A static list of installed toolchains in the UI**: rejected, because it carries no versions and goes stale with the next Dockerfile change.
- **Image labels from build arguments**: rejected, because the .NET SDK, Chromium and Docker come from apt unpinned, so most versions are unknown at build time.
- **Fail the build when the toolchain probe fails**: rejected, because a working image would be discarded over a reporting step, and images that already exist would still need the fallback.
- **A per-role `needsDocker` declaration**: rejected, because a `Restricted` role declaring Docker would be a representable invalid state, and 0098 already makes the container spec branch on the capability profile.

## Consequences

- Every image carries every toolchain, so a repository that needs none of them still runs on the full image.
- Removing the flags changes the persisted `WorkerImageConfiguration` JSON; System.Text.Json ignores the stale members on existing rows.
- With Docker unconditional, the capability gate is what keeps triage and other `Restricted` runs from receiving the Docker allowances.

## Revisions
- 2026-11-14 — A `Full` run gets the 0079 allowances and a daemon only while the operator's nested-Docker switch is on ([ADR 0110](0110-nested-docker-privileges-stay-an-operator-switch.md)).
