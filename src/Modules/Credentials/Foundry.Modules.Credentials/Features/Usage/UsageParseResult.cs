namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Discriminated result returned by <see cref="UsageReportParser.Parse"/> (the <c>/usage</c>
/// report) and <see cref="ProbeTurnUsageParser.Parse"/> (the probe-turn fallback).
/// </summary>
internal abstract record UsageParseResult
{
    private UsageParseResult() { }

    /// <summary>
    /// The log carried complete, well-formed usage figures. The snapshot is ready to store.
    /// </summary>
    internal sealed record Parsed(UsageSnapshot Snapshot) : UsageParseResult;

    /// <summary>
    /// The account has no usage to report: <c>/usage</c> emitted no <c>usage_report</c>
    /// (API-key mode / not reportable), or the probe turn emitted no <c>rate_limit_event</c>
    /// carrying <c>unifiedWindows</c>. The caller should clear the snapshot — this is not a failure.
    /// </summary>
    internal sealed record NoUsageReport : UsageParseResult;

    /// <summary>
    /// <c>/usage</c> emitted a <c>usage_report</c> whose <c>rate_limits</c> (or its <c>limits</c>
    /// rows) is null or absent — a known print-mode regression across CLI versions. The caller
    /// runs the probe-turn fallback within the same poll (ADR 0099) and keeps
    /// <paramref name="ExtraCredits"/>, when <c>/usage</c> still reported them, alongside the
    /// fallback windows.
    /// </summary>
    internal sealed record RateLimitsMissing(UsageExtraCredits? ExtraCredits) : UsageParseResult;

    /// <summary>
    /// The log held no JSON line at all, or the usage data had missing or broken fields.
    /// The caller should retain the last good snapshot and stamp <c>LastRefreshFailedAt</c>.
    /// </summary>
    internal sealed record Unparseable : UsageParseResult;
}
