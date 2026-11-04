using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Features.Providers;

/// <summary>
/// Provider-neutral errors for <see cref="IIssueProvider.UpsertWebhookAsync"/>. Every other
/// provider failure keeps its provider-specific error code.
/// </summary>
internal static class WebhookWriteErrors
{
    public const string InsufficientPermissionCode = "Webhooks.InsufficientPermission";
    public const string NotSupportedCode = "Webhooks.NotSupported";

    public static Error InsufficientPermission(string message) =>
        new(InsufficientPermissionCode, message);

    public static Error NotSupported() =>
        new(NotSupportedCode, "This provider does not support registering webhooks.");
}
