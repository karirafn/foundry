using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>Provider-neutral errors for <see cref="IIssueProvider.RetryCiJobAsync"/>.</summary>
internal static class CiJobRetryErrors
{
    public const string NotSupportedCode = "CiJobRetry.NotSupported";
    public const string RefusedCode = "CiJobRetry.Refused";

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support retrying CI jobs.");

    public static Error Refused(long jobId, int statusCode) =>
        new(RefusedCode, $"CI job {jobId} could not be retried (HTTP {statusCode}).");
}
