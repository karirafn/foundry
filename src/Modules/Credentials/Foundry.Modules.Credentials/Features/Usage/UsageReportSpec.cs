namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Parameters for running a transient usage container.
/// OAuth-only: <c>CanReportUsage</c> gates to OAuth upstream, so no API-key branch is needed.
/// </summary>
internal sealed record UsageReportSpec(UsageReportCommand Command, int TimeoutSeconds)
{
    /// <summary>The slash command whose print-mode answer carries the <c>usage_report</c>.</summary>
    internal const string UsageSlashCommand = "/usage";

    /// <summary>
    /// A minimal, deterministic prompt for the fallback probe turn. A real turn makes the CLI emit a
    /// <c>rate_limit_event</c> with <c>unifiedWindows</c>, which keeps the session and weekly meters
    /// live when <c>/usage</c> print mode returns no <c>rate_limits</c>.
    /// </summary>
    internal const string ProbePrompt = "Reply with: ok";

    internal const int DefaultTimeoutSeconds = 90;
}
