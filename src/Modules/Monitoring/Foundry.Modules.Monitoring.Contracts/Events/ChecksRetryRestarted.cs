using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The checks of an issue whose failing CI jobs were retried read pending: the retried jobs restarted, so the next
/// red read is their verdict rather than the one the retry answered.
/// </summary>
public sealed record ChecksRetryRestarted(MonitoredRepositoryId RepositoryId, int IssueNumber) : IIntegrationEvent;
