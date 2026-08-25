using System.Collections.Frozen;
using System.Net;
using System.Net.Sockets;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Domain.Services;

/// <summary>
/// Domain service that validates whether a provider base URL host is allowed and
/// does not resolve to a private/loopback/link-local address (SSRF DNS rebinding guard).
/// </summary>
internal sealed class ProviderHostGuard(
    IGlobalSettingsQueries globalSettingsQueries,
    IHostAddressResolver hostAddressResolver)
{
    private static readonly FrozenSet<string> ImplicitAllowedHosts = new[]
    {
        "github.com",
        "gitlab.com",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public async Task<Result> EnsureAllowedAsync(BaseUrl baseUrl, CancellationToken cancellationToken)
    {
        string host = baseUrl.Value.Host;

        bool inImplicitSet = ImplicitAllowedHosts.Contains(host);
        if (!inImplicitSet)
        {
            IReadOnlyList<string> operatorHosts =
                await globalSettingsQueries.GetAllowedProviderHostsAsync(cancellationToken);

            bool inOperatorSet = operatorHosts.Any(
                h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));

            if (!inOperatorSet)
            {
                return ProviderHostErrors.NotAllowed(host);
            }
        }

        IReadOnlyList<IPAddress> addresses =
            await hostAddressResolver.ResolveAsync(host, cancellationToken);

        foreach (IPAddress address in addresses)
        {
            if (IsPrivateOrReserved(address))
            {
                return ProviderHostErrors.ResolvesToPrivateAddress(host);
            }
        }

        return Result.Ok();
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        // Unmap IPv4-mapped IPv6 addresses (e.g. ::ffff:10.0.0.1 → 10.0.0.1)
        // so the IPv4 range checks below apply correctly.
        IPAddress effective = address.IsIPv4MappedToIPv6
            ? address.MapToIPv4()
            : address;

        if (IPAddress.IsLoopback(effective))
        {
            return true;
        }

        if (effective.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsPrivateIPv4(effective);
        }

        if (effective.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IsPrivateIPv6(effective);
        }

        return false;
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        // 169.254.0.0/16 — IPv4 link-local
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        // 10.0.0.0/8 — RFC 1918
        if (bytes[0] == 10)
        {
            return true;
        }

        // 172.16.0.0/12 — RFC 1918 (172.16.x.x through 172.31.x.x)
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            return true;
        }

        // 192.168.0.0/16 — RFC 1918
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        return false;
    }

    private static bool IsPrivateIPv6(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        // fe80::/10 — IPv6 link-local
        // First byte is 0xfe, second byte has top 2 bits 10 (i.e. 0x80–0xbf)
        if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
        {
            return true;
        }

        // fc00::/7 — IPv6 unique-local (covers fc00:: through fdff::)
        // First byte has top 7 bits equal to 0b1111110 (0xfc or 0xfd)
        if ((bytes[0] & 0xfe) == 0xfc)
        {
            return true;
        }

        return false;
    }
}
