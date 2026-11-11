using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// Raised when an issue enters awaiting input (ADR 0096). Monitoring nudges its awaiting-input step from it, so the
/// questions are posted straight away rather than on the next scheduled poll.
/// </summary>
public sealed record IssueEnteredAwaitingInput(MonitoredRepositoryId RepositoryId, int IssueNumber) : IIntegrationEvent;
