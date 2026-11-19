namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Host configuration for webhook ingress (ADR 0085, ADR 0116), bound from the <c>Webhooks</c> section.
/// The WebApi always binds a second, webhook-only Kestrel endpoint on <see cref="Port"/>;
/// <c>Webhooks:Port</c> only overrides <see cref="DefaultPort"/>.
/// </summary>
public sealed class WebhookIngressOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>The configuration key of <see cref="Port"/>, read before the host is built to bind Kestrel.</summary>
    public const string PortKey = $"{SectionName}:{nameof(Port)}";

    /// <summary>The webhook port when <see cref="PortKey"/> is unset.</summary>
    public const int DefaultPort = 5099;

    /// <summary>The only paths the webhook port serves, and that no other port serves.</summary>
    public const string PathPrefix = "/webhooks";

    public int Port { get; set; } = DefaultPort;
}
