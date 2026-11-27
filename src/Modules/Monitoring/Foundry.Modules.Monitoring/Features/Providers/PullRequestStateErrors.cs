using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>Provider-neutral errors for <see cref="IIssueProvider.GetPullRequestStateAsync"/>.</summary>
internal static class PullRequestStateErrors
{
    public const string UnrecognisedCode = "PullRequestState.Unrecognised";

    public const string MissingHeadCode = "PullRequestState.MissingHead";

    public static Error Unrecognised(string? state) =>
        new(UnrecognisedCode, $"The provider reported an unrecognised pull request state '{state ?? "<null>"}'.");

    public static Error MissingHead() =>
        new(MissingHeadCode, "The provider reported an open pull request without a valid head commit.");
}
