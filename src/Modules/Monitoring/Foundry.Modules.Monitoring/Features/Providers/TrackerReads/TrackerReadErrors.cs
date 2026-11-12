using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.TrackerReads;

/// <summary>Provider-neutral errors for the tracker reads on <see cref="IIssueProvider"/>.</summary>
internal static class TrackerReadErrors
{
    public const string NotSupportedCode = "TrackerRead.NotSupported";

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support reading the tracker around an issue.");
}
