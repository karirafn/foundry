using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.UnitTests.Modules.Settings.Features;

/// <summary>A webhook ingress probe that reports a fixed port and a fixed result, recording each URL it verifies.</summary>
internal sealed class StubWebhookIngressProbe(WebhookProbeResult? result = null) : IWebhookIngressProbe
{
    internal const int DefaultPort = 5099;

    private readonly WebhookProbeResult _result = result ?? new WebhookProbeResult.Reached();

    public int Port => DefaultPort;

    public List<string> VerifiedUrls { get; } = [];

    public Task<WebhookProbeResult> VerifyAsync(string publicBaseUrl, CancellationToken cancellationToken)
    {
        VerifiedUrls.Add(publicBaseUrl);
        return Task.FromResult(_result);
    }
}
