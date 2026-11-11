using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>Provider-neutral errors for <see cref="IIssueProvider.GetIssueContentAsync"/>.</summary>
internal static class IssueContentErrors
{
    public const string NotSupportedCode = "IssueContent.NotSupported";

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support reading an issue's content.");
}
