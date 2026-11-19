namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Verifies that a Webhook Public Base URL reaches this Foundry process (ADR 0116): a one-time code is sent
/// through the URL to the webhook port's probe path, and only its arrival here counts as success.
/// </summary>
public interface IWebhookIngressProbe
{
    /// <summary>The port the webhook ingress is bound on.</summary>
    int Port { get; }

    Task<WebhookProbeResult> VerifyAsync(string publicBaseUrl, CancellationToken cancellationToken);
}
