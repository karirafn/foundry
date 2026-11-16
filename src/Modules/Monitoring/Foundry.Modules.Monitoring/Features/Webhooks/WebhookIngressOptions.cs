namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Host configuration for webhook ingress (ADR 0085), bound from the <c>Webhooks</c> section.
/// When <see cref="Port"/> is set the WebApi binds a second, webhook-only Kestrel endpoint on it;
/// when absent no webhook endpoint exists.
/// </summary>
public sealed class WebhookIngressOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>The configuration key of <see cref="Port"/>, read before the host is built to bind Kestrel.</summary>
    public const string PortKey = $"{SectionName}:{nameof(Port)}";

    /// <summary>The only paths the webhook port serves, and that no other port serves.</summary>
    public const string PathPrefix = "/webhooks";

    public int? Port { get; set; }
}
