# Code Decides; Claude Judges Only What Code Cannot

## Context

Foundry is adding pipeline steps (triage, implementation, review) and each one could be built as an agent that decides its own next move. An agent-driven pipeline is quick to build. But its behaviour changes between runs, it can't be unit-tested, and a prompt injection in issue or repository content can steer it. Foundry's existing guardrails (the managed settings of [ADR 0086](0086-worker-policy-in-root-rendered-managed-settings.md), the provider-side rebase of [ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md)) already move decisions from the agent to code one at a time. No rule said so in general.

## Decision

Foundry is deterministic wherever it can be. Code owns every transition, ordering, retry, and side effect on providers and infrastructure. Claude is used only where a decision needs judgment that code cannot express, such as whether an issue is described well enough to implement or what kind of change it asks for.

- **Verdicts, not actions**: a judgment step returns a structured verdict validated against a JSON schema (`claude --print --json-schema`), and Foundry's state machine acts on it. A verdict that fails validation is a step failure handled by code, never a fallback to free-form output.
- **Least capability**: a step gets only the tools its judgment needs. A step that only classifies runs without command-execution tools (`--restricted`).
- **Pinned configuration**: each step's prompt and skills come from a pinned version, so the same input meets the same instructions. A change to a step's instructions is a reviewed change, never something an agent writes for itself at runtime.
- **Generative work stays bounded**: where Claude produces artifacts (code, a pull request), code still decides when the step starts, what it may touch, and what happens after it exits.

## Considered Options

- **Agent-orchestrated pipeline**: one agent, or a chain of agents, decides which step runs next. Rejected because the control flow can't be tested and an injected instruction can redirect it.
- **Deterministic rules for every step, including triage**: rejected because judging whether prose describes an implementable change is the kind of decision code cannot make reliably.

## Consequences

- Each new step starts by asking which part of it actually needs judgment. Everything else is built as ordinary, tested code.
- Verdict schemas are contracts between Foundry and its prompts. Changing one changes both sides together.
