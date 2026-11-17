using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The description of a <c>triage_failed</c> issue changed after the failure and the change outlasted the quiet
/// period (issue #773, ADR 0097): the issue goes back to triage. <paramref name="EditSeenAt"/> is when Foundry first
/// saw the settled body.
/// </summary>
public sealed record TriageFailedIssueEdited(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    DateTimeOffset EditSeenAt) : IIntegrationEvent;
