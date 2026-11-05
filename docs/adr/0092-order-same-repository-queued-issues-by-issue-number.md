# Order Same-Repository Queued Issues by Issue Number

## Context

[ADR 0025](0025-shared-dispatch-order.md) defined the dispatch order key as `(TierRank, Position, DetectedAt, Id)`. Repository positions are unique, so `DetectedAt` only ever decides between queued issues of the same repository and tier. This ADR replaces the `DetectedAt` component of that clause.

Detection time reflects when Foundry noticed an issue, not the order the author intended. On 2026-11-05 the live database held 65,113 same-repository issue pairs. In 4,184 of them the detection order disagreed with the issue-number order. In 508 more, `DetectedAt` was tied, so order fell through to `Id`, a random `Guid`, which made it arbitrary.

## Decision

The key becomes `(TierRank, Position, IssueNumber, Id)`. Within a repository and tier, the lowest issue number is dispatched first. Tier still outranks issue number, so revisions and continuations finish before fresh work begins. `(MonitoredRepositoryId, IssueNumber)` is unique, so `Id` only breaks ties between issues whose repositories share a sentinel position.

The dashboard's ineligible-repository partition uses each repository's real `Position` instead of the `int.MaxValue` sentinel. That keeps a repository's issues together and in number order. The sentinel remains only as the fallback for a repository with no display info.

## Considered Options

- **Issue number above tier, `(Position, IssueNumber, TierRank, Id)`**: rejected. A revision on a high-numbered issue would wait behind fresh low-numbered work. It would also let repository position outrank tier globally, reversing ADR 0025.
- **Keep `DetectedAt`**: rejected. It orders by when Foundry noticed an issue rather than by author intent, and ties fall back to a random `Guid`.

## Consequences

An issue that gets unblocked, or an older issue that is reopened or labelled late, goes ahead of newer queued work in the same repository and tier. This is intended. Newly filed issues always have higher numbers, so this rule cannot starve queued work.
