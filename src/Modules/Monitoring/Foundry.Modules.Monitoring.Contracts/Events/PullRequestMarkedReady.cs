using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Foundry marked the issue's pull request ready for review, or found it already ready. The issue records
/// <paramref name="PullRequestUrl"/> so the pull request is marked at most once and a person's later re-draft stands.
/// </summary>
public sealed record PullRequestMarkedReady(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string PullRequestUrl) : IIntegrationEvent;
