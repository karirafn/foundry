# Probe Target Selection: First Visible Repo at Credential Add, Exact Repo at Eligibility

## Context

Write-permission probes need a target repository. Two distinct moments require a probe:
credential add time (no specific repository yet) and repository eligibility evaluation
(an exact repository is known).

## Decision

**At credential add time — probe the first token-visible repository in the namespace.**
When a user adds a credential, no specific repository is nominated yet. The probe runs against
the first repository returned by `ListRepositoriesAsync` that falls within the credential's
namespace. This provides fast fail-fast feedback: a token missing write permissions is rejected
before any repository is associated. If no repositories are visible in the namespace, the probe
cannot run and the credential add is blocked with a dual-cause message (no repositories in namespace,
or the token lacks list access).

**At eligibility evaluation time — probe the exact repository.**
When a repository is evaluated for eligibility (on add or recheck), the exact slug is known.
The probe runs against that specific repository. This is the authoritative per-repository gate:
a token may have write access to some repositories in a namespace but not others, and only a
probe against the exact repository reveals the true permission state for that repository.

## Consequences

- Credential add uses a representative repository as a fast pre-check; it may pass for tokens
  that lack access to a specific repository added later.
- Eligibility evaluation is the authoritative gate — it cannot be bypassed by a successful
  credential add.
- A namespace with zero token-visible repositories blocks credential add regardless of write
  permission state, since no probe target can be selected.
