using System.Collections.Concurrent;
using System.Security.Authentication;
using System.Security.Cryptography;

using Foundry.Modules.Monitoring.Contracts;

using Microsoft.Extensions.Options;

namespace Foundry.Modules.Monitoring.Features.Webhooks.Ingress;

/// <summary>
/// The webhook ingress self-probe (ADR 0116). Holds the codes of probes in flight in this process, so a probe
/// succeeds only when its code arrives here — a URL that routes to another machine or install never passes.
/// </summary>
internal sealed class WebhookIngressProbe(IHttpClientFactory httpClientFactory, IOptions<WebhookIngressOptions> options)
    : IWebhookIngressProbe
{
    internal const string HttpClientName = "WebhookIngressProbe";
    internal const string ProbePathPrefix = "/webhooks/probe/";
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const int CodeByteCount = 32;

    // Code → whether it has arrived. A code is present only while its probe is in flight.
    private readonly ConcurrentDictionary<string, bool> _pending = new(StringComparer.Ordinal);

    public int Port => options.Value.Port;

    public async Task<WebhookProbeResult> VerifyAsync(string publicBaseUrl, CancellationToken cancellationToken)
    {
        string code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(CodeByteCount));
        _pending[code] = false;
        try
        {
            WebhookProbeFailure? failure = await SendAsync($"{publicBaseUrl.TrimEnd('/')}{ProbePathPrefix}{code}", code, cancellationToken);
            return failure is null
                ? new WebhookProbeResult.Reached()
                : new WebhookProbeResult.Unreachable(failure);
        }
        finally
        {
            _pending.TryRemove(code, out _);
        }
    }

    /// <summary>
    /// Records that <paramref name="code"/> reached this process. Only a pending code is recorded; any other is
    /// refused, so the probe path reveals nothing beyond whether a code is pending.
    /// </summary>
    internal bool TryRecordArrival(string code) => _pending.TryUpdate(code, true, false) || IsArrived(code);

    private bool IsArrived(string code) => _pending.TryGetValue(code, out bool arrived) && arrived;

    private async Task<WebhookProbeFailure?> SendAsync(string url, string code, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            int status = (int)response.StatusCode;
            return status switch
            {
                >= 200 and < 300 when IsArrived(code) => null,
                >= 200 and < 300 or 404 => new WebhookProbeFailure.OtherInstance(),
                _ => new WebhookProbeFailure.HttpStatus(status),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookProbeFailure.Timeout();
        }
        catch (HttpRequestException exception)
        {
            return Classify(exception);
        }
    }

    // Transport failures other than DNS and TLS (refused or reset connections) mean no response arrived,
    // which the operator fixes the same way as a timeout: start or re-point the tunnel or proxy.
    private static WebhookProbeFailure Classify(HttpRequestException exception) =>
        exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => new WebhookProbeFailure.Dns(),
            HttpRequestError.SecureConnectionError => new WebhookProbeFailure.Tls(),
            _ when exception.InnerException is AuthenticationException => new WebhookProbeFailure.Tls(),
            _ => new WebhookProbeFailure.Timeout(),
        };
}
