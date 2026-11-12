using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.TrackerReads;

/// <summary>Provider-neutral errors for the tracker reads on <see cref="IIssueProvider"/>.</summary>
internal static class TrackerReadErrors
{
    public const string NotSupportedCode = "TrackerRead.NotSupported";

    public const string NotAnIssueCode = "TrackerRead.NotAnIssue";

    public const string ReadThrewCode = "TrackerRead.Threw";

    public static Error NotAnIssue() =>
        new(NotAnIssueCode, "The number names something other than an issue, such as a pull request.");

    public static Error ReadThrew(Exception exception) =>
        new(ReadThrewCode, $"The tracker read threw {exception.GetType().Name}: {exception.Message}");

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support reading the tracker around an issue.");
}
