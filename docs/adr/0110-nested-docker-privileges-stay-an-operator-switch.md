# Nested Docker Privileges Stay an Operator Switch

## Context

[ADR 0109](0109-the-worker-image-installs-every-toolchain-and-reports-what-it-has.md) installs Docker in every worker image and gives the rootless Docker allowances of [ADR 0079](0079-relaxed-allowances-for-nested-rootless-docker.md) to every `Full` run.
The removed `InstallDocker` setting did two jobs: it installed Docker, and it decided whether a run received `SYS_ADMIN`, `DAC_OVERRIDE` and unconfined seccomp and AppArmor.
Removing it without a replacement would make those allowances permanent for every implement run, with no way for the operator to opt back out.
This replaces 0109's Decision clause "`Full` runs get the 0079 allowances and the entrypoint starts `dockerd`".

## Decision

A boolean setting on `GlobalSettings`, outside `WorkerImageConfiguration`, decides whether implement runs get nested Docker.
A run gets the 0079 allowances and a daemon only when its capability profile is `Full` and the setting is on.
`Restricted` runs never get them.
Changing the setting rebuilds nothing, because it governs privileges, not image contents.
Its initial value is the `InstallDocker` value the existing settings row holds, and the Settings page states the security cost beside the switch, as 0079 required of the old toggle.
With the setting off, a `Full` run has no `DOCKER_HOST`, and the prompt's existing branch tells the agent that integration tests are verified by CI.

## Considered Options

- **No switch, a permanent warning in Settings**: rejected, because the only way to remove the allowances would be a code change, and a configuration that starts unsafe with no way back violates safe defaults.
- **Keep the install toggle**: rejected by 0109, because installing Docker is harmless and only the privileges carry risk.
