using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWebhookSettingsTests;

/// <summary>Stands in for the webhook ingress self-probe: answers a fixed result and records each URL it verifies.</summary>
internal sealed class StubWebhookIngressProbe(int port, WebhookProbeResult result) : IWebhookIngressProbe
{
    private readonly List<string> _verifiedUrls = [];

    public int Port => port;

    public IReadOnlyList<string> VerifiedUrls
    {
        get
        {
            lock (_verifiedUrls)
            {
                return [.. _verifiedUrls];
            }
        }
    }

    public Task<WebhookProbeResult> VerifyAsync(string publicBaseUrl, CancellationToken cancellationToken)
    {
        lock (_verifiedUrls)
        {
            _verifiedUrls.Add(publicBaseUrl);
        }

        return Task.FromResult(result);
    }
}
