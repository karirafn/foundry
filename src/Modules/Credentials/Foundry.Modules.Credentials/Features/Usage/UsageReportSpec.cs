namespace Foundry.Modules.Credentials.Features.Usage;

/// <summary>
/// Parameters for running a transient usage-report container.
/// OAuth-only: <c>CanReportUsage</c> gates to OAuth upstream, so no API-key branch is needed.
/// </summary>
internal sealed record UsageReportSpec(int TimeoutSeconds)
{
    internal const string UsagePrompt = "/usage";
    internal const int DefaultTimeoutSeconds = 60;
}
