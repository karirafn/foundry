using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Raised by Monitoring's rebase step when the post-run rebase of a rebase-pending issue's branch resolved
/// (ADR 0087). <see cref="Outcome"/> is one of <c>up_to_date</c>, <c>rebased</c>, <c>disabled</c>,
/// <c>skipped</c> or <c>conflict</c>; <see cref="Reason"/> is set for <c>skipped</c>
/// (see <see cref="RebaseSkipReasons"/>).
/// </summary>
public sealed record RebaseResolved(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string Outcome,
    string? Reason = null) : IIntegrationEvent;
