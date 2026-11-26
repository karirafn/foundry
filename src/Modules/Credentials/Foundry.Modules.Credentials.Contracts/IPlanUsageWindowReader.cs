namespace Foundry.Modules.Credentials.Contracts;

/// <summary>
/// Reads the plan usage windows a Claude Code stream-json output reports in its <c>rate_limit_event</c> lines, so
/// a caller never needs the CLI's window key names.
/// </summary>
public interface IPlanUsageWindowReader
{
    /// <summary>
    /// The first and the last <c>rate_limit_event</c> among <paramref name="jsonLines"/> that carries
    /// <c>rate_limit_info.unifiedWindows</c>. A line that is not JSON, and a malformed event or window, is skipped.
    /// </summary>
    PlanUsageWindows Read(IEnumerable<string> jsonLines);
}
