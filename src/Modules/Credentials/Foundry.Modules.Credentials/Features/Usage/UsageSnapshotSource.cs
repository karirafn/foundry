namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Which CLI output a <see cref="UsageSnapshot"/> was read from (ADR 0097).
/// Serialized on the wire as a camelCase string (<c>usageReport</c> | <c>probeTurn</c>).
/// </summary>
internal enum UsageSnapshotSource
{
    /// <summary>The primary source: the <c>usage_report</c> of <c>claude -p "/usage"</c>.</summary>
    UsageReport,

    /// <summary>
    /// The fallback: the <c>rate_limit_event</c> of a minimal real turn, run when <c>/usage</c>
    /// emitted a <c>usage_report</c> without <c>rate_limits</c>.
    /// </summary>
    ProbeTurn,
}
