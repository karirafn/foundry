# Live Agent Activity from stream-json over the Container Log

## Context

The worker runs `claude -p --output-format json`, which prints a single result object only when the run exits, so the dashboard cannot show which step, tool, or subagent a running worker is on.
Claude Code offers three ways to observe a run while it executes: `--output-format stream-json` on stdout, HTTP hooks that POST each event to a URL, and OpenTelemetry export to a collector.
Worker containers have no network path back to Foundry today, the WebApi is unauthenticated and bound to localhost (#178), and worker egress is due to be restricted (#61).

## Decision

The entrypoint runs `claude` with `--output-format stream-json --verbose` and streams stdout as it is produced.
Foundry reads the events from the Docker log stream it already follows, and parses them into an in-memory per-run projection (current task list, active subagents and skills, in-flight tool, token totals, last event time).
The projection is not persisted: after a Foundry restart it is rebuilt by re-reading the container log from the start, which lives exactly as long as the run.
Following ADR 0063, the projection reaches the dashboard over `WorkerHub` and is replayed on connect.

Steps are read from the agent's own signals (`TaskCreate`/`TaskUpdate`, `Agent` dispatches, `Skill` calls, `task_*` system events) rather than a Foundry-defined stage vocabulary, so any workflow is displayed without configuration.

## Considered Options

- **HTTP hooks** — rejected: requires a reachable Foundry URL, a per-run credential, an egress exception under #61, and mixes telemetry into hook configuration that #606 hands to the operator's host settings.
- **OpenTelemetry** — rejected for live display: same reachability and auth cost; metrics export on a 60 s default interval, tool details are gated off by default, and events identify subagents only by `agent_id`. Remains a candidate for organisation-level cost reporting.
- **Persisting the projection on `ActiveRun`** — rejected: one write per tool call for presentation-only state, contrary to ADR 0063's "store only what is true right now and needed" stance.

## Consequences

No new inbound endpoint or trust boundary is introduced; the existing secret redaction on the log stream applies to every event.
Post-exit outcome parsing must select the last `type: "result"` event and must keep the tail, not the head, when truncating the captured output, because stream-json output is far larger than the single result line it replaces and one run can emit more than one `result` event.
Log views render a readable transcript produced by the same server-side parser rather than raw JSON.
Docker logs grow with every tool call; re-reading them on restart costs time proportional to the run's length.
