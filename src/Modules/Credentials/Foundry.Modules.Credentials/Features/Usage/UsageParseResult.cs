namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Discriminated result returned by <see cref="UsageReportParser.Parse"/>, representing
/// the three distinct outcomes of parsing a <c>claude -p "/usage" --output-format stream-json</c> log.
/// </summary>
internal abstract record UsageParseResult
{
    private UsageParseResult() { }

    /// <summary>
    /// The log contained a valid <c>usage_report</c> with all required fields.
    /// The snapshot is complete and ready to store.
    /// </summary>
    internal sealed record Parsed(UsageSnapshot Snapshot) : UsageParseResult;

    /// <summary>
    /// No assistant message in the log carried a <c>usage_report</c>, or
    /// <c>usage_report.rate_limits</c> was null (usage-based billing or API-key mode).
    /// The caller should clear the snapshot — this is not a failure.
    /// </summary>
    internal sealed record NoUsageReport : UsageParseResult;

    /// <summary>
    /// The log was malformed JSON or the <c>usage_report</c> was missing required fields.
    /// The caller should retain the last good snapshot and stamp <c>LastRefreshFailedAt</c>.
    /// </summary>
    internal sealed record Unparseable : UsageParseResult;
}
