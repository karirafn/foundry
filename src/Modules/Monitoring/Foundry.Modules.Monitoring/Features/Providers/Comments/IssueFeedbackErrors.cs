using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers.Comments;

/// <summary>Provider-neutral errors for <see cref="IIssueProvider.GetIssueFeedbackAsync"/>.</summary>
internal static class IssueFeedbackErrors
{
    public const string NotSupportedCode = "IssueFeedback.NotSupported";

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support reading issue comment threads.");
}
