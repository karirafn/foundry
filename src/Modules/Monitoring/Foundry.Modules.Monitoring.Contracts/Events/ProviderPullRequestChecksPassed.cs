using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// A review issue whose checks verdict is not passed read green on the same head commit (a rerun or retry of the
/// pipeline), so the checks gate should confirm and route the green verdict (ADR 0108).
/// </summary>
public sealed record ProviderPullRequestChecksPassed(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber) : IIntegrationEvent;
