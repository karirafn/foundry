---
reaches: hidden-invariant
---
# Each provider's pull request state is interpreted once, by its adapter

## Context
Monitoring read a pull request's provider state in three places per provider (status, by-branch, tracker),
each with its own string mapping. They drifted: GitHub's status read ignored `MERGED`, so a merged pull
request read as open, and GitLab's by-branch read mapped `locked` to closed while its other reads mapped it
to open.

## Decision
Each provider adapter owns one parser from its state vocabulary to `MergeRequestPresence`
(`Open | Merged | Closed`; never `None`), in `GitHubHttpClient.PullRequestState.cs` and
`GitLabHttpClient.PullRequestState.cs`. Every read of a pull request's state goes through it: the status
read builds the closed `PullRequestState` (`Open(Head, IsDraft) | Merged | Closed`) from it, and the
by-branch and tracker reads map it to `MergeRequestPresence` and `ClosingMergeRequestState`. An
unrecognised state, or an open pull request without a valid head, is a failed status read. GitLab `locked`
is `Open`.

## Consequences
A new provider read that needs pull request state calls its adapter's parser rather than matching strings.
Callers never see a merged-but-open or headless-open pull request.
