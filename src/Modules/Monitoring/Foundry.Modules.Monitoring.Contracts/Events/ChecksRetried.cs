using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Foundry retried every failing CI job of the red verdict read on <paramref name="HeadSha"/>, the head a checks
/// retry was owed for, at <paramref name="RetriedAt"/>. The issue waits on the retried jobs' verdict.
/// </summary>
public sealed record ChecksRetried(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string HeadSha,
    DateTimeOffset RetriedAt) : IIntegrationEvent;
