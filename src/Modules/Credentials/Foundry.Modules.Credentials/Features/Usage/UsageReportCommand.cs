namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>Which CLI command a usage container runs (ADR 0097).</summary>
internal enum UsageReportCommand
{
    /// <summary>
    /// <c>claude -p "/usage" --output-format stream-json --verbose</c> — the primary source:
    /// Anthropic's usage rows, severities and extra-usage credits, with no API turn.
    /// </summary>
    UsageCommand,

    /// <summary>
    /// <c>claude -p "Reply with: ok" --max-turns 1 --output-format stream-json --verbose</c> — the
    /// fallback: a real (cheap) turn whose <c>rate_limit_event</c> carries the session and weekly windows.
    /// </summary>
    ProbeTurn,
}
