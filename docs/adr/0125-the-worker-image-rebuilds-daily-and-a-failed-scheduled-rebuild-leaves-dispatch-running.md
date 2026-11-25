---
reaches: non-functional
---

# The Worker Image Rebuilds Daily and a Failed Scheduled Rebuild Leaves Dispatch Running

## Context

Rebuilds happened only at startup, after a settings change and on a failure retry, so a new .NET major release reached workers only at the next redeploy. Any failed build set `ImageBuildStatus.Failed`, which stops dispatch for every repository.

## Decision

Foundry requests a rebuild 24 hours after the last successful build while image building is enabled. A failed scheduled rebuild is recorded and retried with the existing backoff, and dispatch continues on the current verified image. Startup, settings-change and manual rebuild failures still stop dispatch.

## Considered Options

- **Check the release index in C#**: rejected, because it duplicates the SDK filter that lives in `workers/install-dotnet-sdks.sh`.
- **Manual and restart rebuilds only**: rejected, because the operator would be the only way out.

## Consequences

- About monthly, when the index changes, dispatch pauses for a few minutes while the SDK layers refresh.
- A scheduled failure is visible in Settings but never blocks dispatch.
