namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Discriminated result returned by <see cref="UsageReportParser.Parse"/>, representing
/// the three distinct outcomes of parsing a real Claude CLI turn log in stream-json format.
/// </summary>
internal abstract record UsageParseResult
{
    private UsageParseResult() { }

    /// <summary>
    /// The log contained a valid <c>rate_limit_event</c> with <c>unifiedWindows</c> and all
    /// required fields. The snapshot is complete and ready to store.
    /// </summary>
    internal sealed record Parsed(UsageSnapshot Snapshot) : UsageParseResult;

    /// <summary>
    /// No <c>rate_limit_event</c> with <c>unifiedWindows</c> was found in the log — either
    /// no API turn occurred, the account is in API-key mode, or the CLI version predates
    /// <c>unifiedWindows</c> support (pre-2.1.302). The caller should clear the snapshot —
    /// this is not a failure.
    /// </summary>
    internal sealed record NoUsageReport : UsageParseResult;

    /// <summary>
    /// The log was malformed JSON or a <c>rate_limit_info</c> window was missing required
    /// fields. The caller should retain the last good snapshot and stamp
    /// <c>LastRefreshFailedAt</c>.
    /// </summary>
    internal sealed record Unparseable : UsageParseResult;
}
