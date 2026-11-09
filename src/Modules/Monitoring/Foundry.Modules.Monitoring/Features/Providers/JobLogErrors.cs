using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>Provider-neutral errors for <see cref="IIssueProvider.GetJobLogAsync"/>.</summary>
internal static class JobLogErrors
{
    public const string NotSupportedCode = "JobLogs.NotSupported";
    public const string UnavailableCode = "JobLogs.Unavailable";

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support reading CI job logs.");

    public static Error Unavailable(long jobId, int statusCode) =>
        new(UnavailableCode, $"The log of CI job {jobId} could not be read (HTTP {statusCode}).");
}
