using System.Text.Encodings.Web;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.PackageSources;

namespace Foundry.Modules.Monitoring.Features.PackageSourceProxy;

/// <summary>
/// One Package Source as the Package Source Proxy serves it (ADR 0112): which upstream URLs a proxied request may
/// reach, and how upstream URLs in a JSON body are re-pointed at the proxy.
/// </summary>
/// <remarks>
/// A request reaches upstream only under the service index's directory, so the credential Foundry sends never reaches
/// another API path on the provider host.
/// </remarks>
internal sealed class ProxiedPackageSource
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        // The body is read by NuGet, never rendered as HTML, so only JSON's own escaping is needed.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Uri _serviceIndexUrl;
    private readonly string _directoryPath;
    private readonly string _proxyBase;

    /// <param name="sourceId">The source's id, the proxy path segment that names it.</param>
    /// <param name="serviceIndexUrl">The source's service-index URL.</param>
    /// <param name="proxyBaseUrl">The proxy's base URL as a worker sees it, ending in <c>/package-sources/</c>.</param>
    public ProxiedPackageSource(PackageSourceId sourceId, Uri serviceIndexUrl, Uri proxyBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(serviceIndexUrl);
        ArgumentNullException.ThrowIfNull(proxyBaseUrl);

        _serviceIndexUrl = serviceIndexUrl;
        string path = serviceIndexUrl.AbsolutePath;
        _directoryPath = path[..(path.LastIndexOf('/') + 1)];
        _proxyBase = $"{proxyBaseUrl.AbsoluteUri.TrimEnd('/')}/{sourceId.Value}";
    }

    /// <summary>
    /// The upstream URL for a proxied request whose path below the source id is <paramref name="escapedPath"/> (starting
    /// with <c>/</c>) and whose query is <paramref name="escapedQuery"/>; <see langword="null"/> when it falls outside the
    /// service index's directory. The check runs on the URL as it will be sent, after dot segments are resolved.
    /// </summary>
    public Uri? UpstreamUrlFor(string escapedPath, string escapedQuery)
    {
        ArgumentNullException.ThrowIfNull(escapedPath);
        ArgumentNullException.ThrowIfNull(escapedQuery);

        string origin = _serviceIndexUrl.GetLeftPart(UriPartial.Authority);
        string path = escapedPath.StartsWith('/') ? escapedPath : "/" + escapedPath;

        if (!Uri.TryCreate(origin + path + escapedQuery, UriKind.Absolute, out Uri? url))
        {
            return null;
        }

        return IsUnderDirectory(url) ? url : null;
    }

    /// <summary>
    /// Rewrites every string in a JSON document that is an upstream URL under the service index's directory to the
    /// same path and query on the proxy. Returns <see langword="null"/> when <paramref name="json"/> is not JSON, so the
    /// caller passes the body through as it came.
    /// </summary>
    public byte[]? RewriteJson(ReadOnlyMemory<byte> json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            using MemoryStream output = new();
            using (Utf8JsonWriter writer = new(output, WriterOptions))
            {
                Write(document.RootElement, writer);
            }

            return output.ToArray();
        }
    }

    /// <summary>The proxy URL for <paramref name="value"/>, or <see langword="null"/> when it is not an upstream URL to re-point.</summary>
    internal string? Rewrite(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url)
            || url.Scheme is not ("http" or "https")
            || !IsUnderDirectory(url))
        {
            return null;
        }

        return _proxyBase + url.PathAndQuery;
    }

    private bool IsUnderDirectory(Uri url) =>
        PackageSourceUpstreamClient.SameOrigin(url, _serviceIndexUrl)
        && url.AbsolutePath.StartsWith(_directoryPath, StringComparison.Ordinal);

    private void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                string value = element.GetString()!;
                writer.WriteStringValue(Rewrite(value) ?? value);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
