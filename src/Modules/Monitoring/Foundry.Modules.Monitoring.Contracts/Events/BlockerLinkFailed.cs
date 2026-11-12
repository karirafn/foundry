using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// The provider refused to link a blocker that triage named (ADR 0103). Issues fails the triage verdict of the run
/// that named it, so the issue stops waiting on a link that will never be written.
/// </summary>
/// <param name="WorkerRunId">The triage run that named the blocker; a plain <see cref="Guid"/> as Monitoring does not know the Workers contract.</param>
/// <param name="BlockerNumber">The blocker whose link was refused.</param>
/// <param name="ProviderMessage">The provider's refusal, flattened to one line and capped.</param>
/// <param name="FailedAt">When Monitoring gave up on the link.</param>
public sealed record BlockerLinkFailed(
    MonitoredRepositoryId MonitoredRepositoryId,
    int IssueNumber,
    Guid WorkerRunId,
    int BlockerNumber,
    string ProviderMessage,
    DateTimeOffset FailedAt) : IIntegrationEvent;
