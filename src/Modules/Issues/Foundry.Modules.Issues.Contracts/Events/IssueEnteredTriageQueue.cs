using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts.Events;

/// <summary>
/// Raised when an issue enters the triage queue. Monitoring nudges the repository's dependency pass from it,
/// so blockers linked before the trigger label are read once the issue exists rather than on the next
/// scheduled poll. It carries no issue number because the dependency pass runs per repository.
/// </summary>
// CA1711 suppressed: "TriageQueue" names the domain state the issue entered, not a Queue collection type.
#pragma warning disable CA1711
public sealed record IssueEnteredTriageQueue(MonitoredRepositoryId RepositoryId) : IIntegrationEvent;
#pragma warning restore CA1711
