using System.Net;
using System.Net.NetworkInformation;

using Foundry.Modules.Workers.Features.ContainerSpec;

namespace Foundry.Modules.Workers.Infrastructure;

internal sealed class FoundryHostSystem : IFoundryHostSystem
{
    private const string OsReleasePath = "/proc/sys/kernel/osrelease";

    public string? ReadKernelOsRelease()
    {
        // Absent off Linux; unreadable means "not WSL", which keeps the host-gateway entry.
        try
        {
            return File.ReadAllText(OsReleasePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<HostNetworkInterface> ListNetworkInterfaces()
    {
        return
        [
            .. NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface => networkInterface.OperationalStatus != OperationalStatus.Down)
                .Select(ToHostNetworkInterface),
        ];
    }

    private static HostNetworkInterface ToHostNetworkInterface(NetworkInterface networkInterface)
    {
        IPInterfaceProperties properties = networkInterface.GetIPProperties();
        IPAddress[] gateways = [.. properties.GatewayAddresses.Select(gateway => gateway.Address)];
        IPAddress[] addresses = [.. properties.UnicastAddresses.Select(unicast => unicast.Address)];
        return new HostNetworkInterface(networkInterface.Name, gateways, addresses);
    }
}
