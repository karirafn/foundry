namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Parameters for running a transient usage-report container.
/// OAuth-only: <c>CanReportUsage</c> gates to OAuth upstream, so no API-key branch is needed.
/// </summary>
internal sealed record UsageReportSpec(int TimeoutSeconds)
{
    /// <summary>
    /// A minimal, deterministic prompt that triggers a real (cheap) API turn. A real turn is
    /// the reliable way to cause the CLI to emit a <c>rate_limit_event</c> with
    /// <c>unifiedWindows</c> — the only source of per-window utilization data.
    /// <para>
    /// The <c>/usage</c> print-mode command cannot be used for this purpose because it does not
    /// emit <c>rate_limit_event</c> in stream-json mode, and <c>usage_report.rate_limits</c>
    /// is always null in print mode even on accounts at the rate limit.
    /// </para>
    /// </summary>
    internal const string UsagePrompt = "Reply with: ok";

    internal const int DefaultTimeoutSeconds = 90;
}
