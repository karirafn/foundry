# Two Integration Events for Review Monitoring

## Context

The monitoring module needs to detect two distinct outcomes for issues in the ReviewIssue state: the provider-side issue was closed (indicating the PR was merged and accepted) or the PR was closed without being merged (indicating rejection). A single event with a status discriminator was considered.

## Decision

Publish two separate integration events — `ProviderIssueClosed` and `ProviderPullRequestClosed` — rather than a single event with a status field. Each event maps to a single handler with a single transition: issue-closed triggers `ReviewIssue.Complete()`, PR-closed-without-merge triggers `ReviewIssue.Fail()`.
