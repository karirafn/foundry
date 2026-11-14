using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Raised by Monitoring's merge step when Foundry's merge of a merge-pending issue's pull request resolved
/// (ADR 0105). <see cref="Outcome"/> is one of <c>merged</c>, <c>disabled</c> or <c>skipped</c>;
/// <see cref="Reason"/> is set for <c>skipped</c> (see <see cref="MergeSkipReasons"/>).
/// </summary>
public sealed record MergeResolved(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string Outcome,
    string? Reason = null) : IIntegrationEvent;
