using System.Globalization;

using Microsoft.AspNetCore.Hosting;

namespace Foundry.WebApi;

/// <summary>
/// Binds the optional port-scoped ingress Kestrel endpoints (ADR 0085). Explicit <c>Listen</c> calls would
/// make Kestrel ignore the configured URLs, so each ingress URL is appended to the existing URL list instead,
/// keeping the main endpoint(s) exactly as configured.
/// </summary>
internal static class IngressKestrelBinding
{
    private const string DefaultUrl = "http://localhost:5000";
    private const string HttpPortsKey = "http_ports";
    private const string HttpsPortsKey = "https_ports";

    /// <summary>
    /// Appends an <c>http://*:&lt;port&gt;</c> URL for each configured port under <paramref name="portSettingKeys"/>.
    /// An unset key binds nothing; a value that is not a TCP port, or a port two keys share, fails startup.
    /// </summary>
    internal static void AddIngressEndpoints(WebApplicationBuilder builder, params IReadOnlyList<string> portSettingKeys)
    {
        List<int> ports = ReadPorts(builder, portSettingKeys);
        if (ports.Count == 0)
        {
            return;
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

        foreach (int port in ports)
        {
            string portSuffix = $":{port.ToString(CultureInfo.InvariantCulture)}";
            if (!urls.Any(u => u.TrimEnd('/').EndsWith(portSuffix, StringComparison.Ordinal)))
            {
                urls.Add($"http://*{portSuffix}");
            }
        }

        builder.WebHost.UseUrls([.. urls]);
    }

    private static List<int> ReadPorts(WebApplicationBuilder builder, IReadOnlyList<string> portSettingKeys)
    {
        Dictionary<int, string> keysByPort = [];

        foreach (string key in portSettingKeys)
        {
            string? portSetting = builder.Configuration[key];
            if (string.IsNullOrWhiteSpace(portSetting))
            {
                continue;
            }

            if (!int.TryParse(portSetting, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            {
                throw new InvalidOperationException($"{key} must be a TCP port number, but was '{portSetting}'.");
            }

            if (!keysByPort.TryAdd(port, key))
            {
                throw new InvalidOperationException($"{keysByPort[port]} and {key} are both port {port}; each ingress needs its own port.");
            }
        }

        return [.. keysByPort.Keys];
    }

    private static string[] Split(string? value) =>
        (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
