# Crash-Safe, Self-Cleaning Transient Login and Helper Containers

## Context

The interactive OAuth login flow starts short-lived Docker containers: a login container (`claude auth login --claudeai`) and one or more credential-helper containers (`claude auth status --json` / onboarding seed) that mount the credential volume.

Delivering the authorization code execs `printf … > /tmp/ci; kill $(cat /tmp/ci.pid)` to give the CLI EOF on stdin.
That `kill` removes the sleep-holder — which was the only thing bounding the login container's lifetime.
After delivery, a `claude` process that hangs (observed in practice) keeps the container running indefinitely.

Teardown lived only inside the running `LoginSessionService`, and the host-shutdown path did not tear down at all.
`LoginContainerReaper` ran only at startup and matched only the `foundry.login` label.
Credential-helper containers carried no `foundry.*` labels, so no reaper could ever find them.

The consequence, reproduced this session: when the WebApi host died during the sign-in window, the login container was orphaned — a hung `claude` process holding the credential volume, unreapable until the next restart. Any process death during the login/helper window leaks a container.

## Decision

Make every transient container self-cleaning and reap-safe, so a leak cannot outlive the session timeout regardless of host liveness. Four elements, designed together:

- **Self-terminating login container.** Wrap the CLI as `timeout -k 10 <session-timeout> claude auth login --claudeai`, bounding it independently of the sleep-holder the code-delivery exec kills. Create the login and helper containers with `HostConfig.AutoRemove = true` so Docker removes them on exit with no watcher present. `AutoRemove` is safe here because success is detected from the live log stream while the container runs, and identity is read from the credential volume via a separate helper — the login container's logs are never read after it exits (ADR 0027).
- **Transient label distinct from workers.** All login and helper containers carry `foundry.transient=true` plus a `foundry.role` (`login` / `credential-helper`). Worker containers carry `foundry.managed=true` + `foundry.worker-run-id`; reaping keys strictly on `foundry.transient`, so it can never stop a running worker.
- **Guaranteed in-process teardown.** `LoginSessionService.SubmitCodeAsync` tears the container down in a `finally`, so every exit path — success, invalid code, timeout, host shutdown — removes it exactly once. A second remove is a safe no-op (`StopAsync`/`RemoveAsync` swallow `DockerContainerNotFoundException`, which also composes with `AutoRemove`).
- **Two-layer reaping.** `LoginContainerReaper` (startup, `IHostedLifecycleService`) and a new `TransientContainerReaper` (periodic `PeriodicBackgroundService`, 60 s) both list by `foundry.transient` and reap orphans. Each reap tick is wrapped in `try/catch (Exception ex) when (ex is not OperationCanceledException)` — an unhandled throw would trip `BackgroundServiceExceptionBehavior.StopHost` and kill the host, the exact failure mode being fixed.

## Considered Options

### Rely on the startup reaper alone

Rejected: it only reaps on restart, so a container leaks — holding the credential volume — for the entire gap between crash and next start. The periodic reaper plus container self-termination close that gap.

### Reap by the existing `foundry.managed` label

Rejected: worker containers also carry `foundry.managed`, so reaping on it at startup or on a timer would stop running workers. A dedicated `foundry.transient` label keeps the two lifecycles disjoint.

### `AutoRemove` without the `timeout` wrapper

Rejected: `AutoRemove` only fires when the container exits. A hung `claude` process never exits, so without a `timeout` bound the container would still linger indefinitely. The two together guarantee bounded self-cleanup.

## Consequences

- A login container cannot outlive `<session-timeout>` even if Foundry is dead the entire time: `timeout` ends the CLI, `AutoRemove` deletes the container.
- Leaks are covered at three layers — in-process `finally`, container self-termination, and the periodic/startup reapers — so no single failure (including a silent host crash) leaves an orphan for long.
- The periodic reaper is a background service; its tick is defensively wrapped so a Docker-API hiccup logs and retries next tick rather than stopping the host.
- The login image (`node:22-bookworm-slim`) already ships coreutils `timeout`; no image change is required.
