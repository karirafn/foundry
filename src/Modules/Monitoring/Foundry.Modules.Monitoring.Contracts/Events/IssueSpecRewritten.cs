using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The spec an issue with open work states differs from the one its last triage judged, and the difference outlasted
/// the quiet period (ADR 0125): the issue goes back to triage holding its open work. <paramref name="RewriteSeenAt"/>
/// is when Foundry first saw the settled body.
/// </summary>
public sealed record IssueSpecRewritten(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    DateTimeOffset RewriteSeenAt) : IIntegrationEvent;
