using System.Globalization;
using System.Net;
using System.Net.Sockets;

using Docker.DotNet;

using Microsoft.Extensions.Logging;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>
/// Maps <c>host.docker.internal</c> to Foundry's host. Docker resolves <c>host-gateway</c> to the host it runs on, which
/// is Foundry's host on a native engine. Docker Desktop resolves it to its own address for the Windows host
/// (192.168.65.254 and an IPv6 twin), so when Foundry runs in a WSL2 distro the worker would reach Windows, not the
/// distro where the Package Source Proxy listens; there the distro's default-route IPv4 address is mapped instead.
/// </summary>
internal sealed class WorkerHostEntryResolver(
    ISystemOperations engine,
    IFoundryHostSystem host,
    ILogger<WorkerHostEntryResolver> logger) : IWorkerHostEntryResolver
{
    private const string HostName = "host.docker.internal";
    private const string HostGatewayEntry = HostName + ":host-gateway";
    private const string DockerDesktopOperatingSystem = "Docker Desktop";
    private const string WslKernelMarker = "microsoft";

    // Resolved on every dispatch rather than cached: the WSL NAT address changes across restarts.
    public async Task<string> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!RunsInWsl())
        {
            return HostGatewayEntry;
        }

        string engineOperatingSystem;
        try
        {
            engineOperatingSystem = (await engine.GetSystemInfoAsync(cancellationToken)).OperatingSystem;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Foundry runs in WSL but the Docker engine's system info could not be read; workers fall back to {Entry}.",
                HostGatewayEntry);
            return HostGatewayEntry;
        }

        if (!string.Equals(engineOperatingSystem, DockerDesktopOperatingSystem, StringComparison.Ordinal))
        {
            return HostGatewayEntry;
        }

        if (DefaultRouteIpv4Address() is not { } address)
        {
            logger.LogWarning(
                "Foundry runs in WSL against Docker Desktop but no default-route IPv4 address was found; workers fall back to "
                + "{Entry}, which Docker Desktop resolves to the Windows host, so they may not reach the Package Source Proxy.",
                HostGatewayEntry);
            return HostGatewayEntry;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{HostName}:{address}");
    }

    private bool RunsInWsl()
    {
        return host.ReadKernelOsRelease() is { } osRelease
            && osRelease.Contains(WslKernelMarker, StringComparison.OrdinalIgnoreCase);
    }

    private IPAddress? DefaultRouteIpv4Address()
    {
        HostNetworkInterface? defaultRoute = host.ListNetworkInterfaces()
            .FirstOrDefault(networkInterface => networkInterface.Gateways.Any(IsIpv4Gateway));

        return defaultRoute?.Addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
    }

    private static bool IsIpv4Gateway(IPAddress gateway)
    {
        return gateway.AddressFamily == AddressFamily.InterNetwork && !gateway.Equals(IPAddress.Any);
    }
}
