using System.Net;

namespace Foundry.Modules.Workers.Features.ContainerSpec;

/// <summary>One network interface of Foundry's host: the gateways its routes go through and its own addresses.</summary>
internal sealed record HostNetworkInterface(
    string Name,
    IReadOnlyList<IPAddress> Gateways,
    IReadOnlyList<IPAddress> Addresses);
