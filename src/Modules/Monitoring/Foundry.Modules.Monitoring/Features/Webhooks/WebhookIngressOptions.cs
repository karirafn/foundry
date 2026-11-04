namespace Foundry.Modules.Monitoring.Features.Webhooks;

/// <summary>
/// Host configuration for webhook ingress (ADR 0085), bound from the <c>Webhooks</c> section.
/// When <see cref="Port"/> is set the WebApi binds a second, webhook-only Kestrel endpoint on it;
/// when absent no webhook endpoint exists.
/// </summary>
public sealed class WebhookIngressOptions
{
    public const string SectionName = "Webhooks";

    public int? Port { get; set; }
}
