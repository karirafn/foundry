using System.Globalization;

using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.AspNetCore.Hosting;

namespace Foundry.WebApi;

/// <summary>
/// Binds the optional webhook-only Kestrel endpoint (ADR 0085). Explicit <c>Listen</c> calls would make
/// Kestrel ignore the configured URLs, so the webhook URL is appended to the existing URL list instead,
/// keeping the main endpoint(s) exactly as configured.
/// </summary>
internal static class WebhookKestrelBinding
{
    private const string DefaultUrl = "http://localhost:5000";
    private const string HttpPortsKey = "http_ports";
    private const string HttpsPortsKey = "https_ports";

    internal static void AddWebhookEndpoint(WebApplicationBuilder builder)
    {
        string? portSetting = builder.Configuration[$"{WebhookIngressOptions.SectionName}:{nameof(WebhookIngressOptions.Port)}"];
        if (string.IsNullOrWhiteSpace(portSetting))
        {
            return;
        }

        if (!int.TryParse(portSetting, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Webhooks:Port must be a TCP port number, but was '{portSetting}'.");
        }

        List<string> urls = [.. Split(builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey))];

        if (urls.Count == 0)
        {
            urls.AddRange(Split(builder.WebHost.GetSetting(HttpPortsKey)).Select(p => $"http://*:{p}"));
            urls.AddRange(Split(builder.WebHost.GetSetting(HttpsPortsKey)).Select(p => $"https://*:{p}"));
        }

        if (urls.Count == 0)
        {
            urls.Add(DefaultUrl);
        }

        string portSuffix = $":{port.ToString(CultureInfo.InvariantCulture)}";
        if (!urls.Any(u => u.TrimEnd('/').EndsWith(portSuffix, StringComparison.Ordinal)))
        {
            urls.Add($"http://*{portSuffix}");
        }

        builder.WebHost.UseUrls([.. urls]);
    }

    private static string[] Split(string? value) =>
        (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
