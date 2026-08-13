# Probe classification exposed via IProbeOutcomeClassifier in Workers.Contracts

## Context

The credit probe runs in the Credentials module, but the money-vs-time-vs-infrastructure classification of a container's output lives in the Workers module's `IContainerOutputParser`, which is `internal`. Duplicating the reset-time/429 discrimination in Credentials would create two sources of truth for the same brittle parsing.

## Decision

Add a public `IProbeOutcomeClassifier` and `ProbeOutcome` to `Workers.Contracts`, implemented in Workers over the existing `IContainerOutputParser`. Credentials depends only on `Workers.Contracts` (already referenced) and consumes the coarser `ProbeOutcome` (`Available`, `CreditsStillBlocked`, `UsageLimited`, `InfrastructureFailure`).

## Consequences

One source of truth for probe-log classification; the parser stays `internal`. Adding a new `ContainerOutputParseResult` variant requires updating the classifier's mapping. The probe never sees the full parse-result taxonomy — only the four outcomes it acts on — which is the intended narrowing.
