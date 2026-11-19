using System.Net;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Shared;

namespace Foundry.Modules.Settings.Domain.ValueObjects;

/// <summary>
/// The public base URL GitHub and GitLab deliver webhooks to: an absolute <c>https</c> URL without query,
/// fragment or userinfo, whose host a provider can reach — not <c>localhost</c> or a loopback, link-local or
/// unspecified IP address (ADR 0116). The host is never resolved, so private ranges and every other DNS name
/// pass. A trailing slash is trimmed so the payload URL is built as
/// <c>&lt;base&gt;/webhooks/{github|gitlab}/&lt;hookId&gt;</c>.
/// </summary>
public sealed record WebhookPublicBaseUrl
{
    private const string LocalhostName = "localhost";

    private WebhookPublicBaseUrl(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<WebhookPublicBaseUrl> Create(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.Length > GlobalSettings.MaxWebhookPublicBaseUrlLength
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrEmpty(uri.Host))
        {
            return Result<WebhookPublicBaseUrl>.Fail(SettingsErrors.InvalidWebhookPublicBaseUrl);
        }

        if (IsUndeliverableHost(uri))
        {
            return Result<WebhookPublicBaseUrl>.Fail(SettingsErrors.UndeliverableWebhookHost);
        }

        return Result<WebhookPublicBaseUrl>.Ok(new WebhookPublicBaseUrl(trimmed.TrimEnd('/')));
    }

    // Stored values were normalized when written; a row that predates normalization is wrapped as-is
    // rather than failing the load.
    internal static WebhookPublicBaseUrl Restore(string storedValue) => new(storedValue);

    private static bool IsUndeliverableHost(Uri uri)
    {
        if (uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            return string.Equals(uri.IdnHost.TrimEnd('.'), LocalhostName, StringComparison.OrdinalIgnoreCase);
        }

        IPAddress address = IPAddress.Parse(uri.DnsSafeHost);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || IsIPv4LinkLocal(address);
    }

    private static bool IsIPv4LinkLocal(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}
