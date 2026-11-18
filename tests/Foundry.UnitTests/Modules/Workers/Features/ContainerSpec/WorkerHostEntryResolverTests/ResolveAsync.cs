using System.Net;

using Foundry.Modules.Workers.Features.ContainerSpec;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Workers;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.ContainerSpec.WorkerHostEntryResolverTests;

public sealed class ResolveAsync
{
    // Captured from `docker info --format '{{.OperatingSystem}}'` on Docker Desktop 29.9.
    private const string DockerDesktop = "Docker Desktop";

    // Captured from /proc/sys/kernel/osrelease in a WSL2 distro.
    private const string WslOsRelease = "6.6.87.2-microsoft-standard-WSL2\n";

    private static readonly HostNetworkInterface Loopback = new(
        "lo",
        Gateways: [],
        Addresses: [IPAddress.Loopback, IPAddress.IPv6Loopback]);

    private static readonly HostNetworkInterface WslEth0 = new(
        "eth0",
        Gateways: [IPAddress.Parse("172.20.0.1")],
        Addresses: [IPAddress.Parse("fe80::215:5dff:fe12:3456"), IPAddress.Parse("172.20.5.9")]);

    private readonly CapturingLogger<WorkerHostEntryResolver> _logger = new();

    private WorkerHostEntryResolver BuildResolver(FakeSystemOperations engine, FakeFoundryHostSystem host)
    {
        return new WorkerHostEntryResolver(engine, host, _logger);
    }

    [Fact]
    public async Task WhenDockerDesktopAndFoundryRunsInWsl_MapsTheDefaultRouteInterfaceAddress()
    {
        // Arrange
        FakeFoundryHostSystem host = new(WslOsRelease, [Loopback, WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem(DockerDesktop), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:172.20.5.9");
    }

    [Fact]
    public async Task WhenAnotherInterfaceHasAnAddressButNoGateway_MapsTheDefaultRouteInterfaceAddress()
    {
        // Arrange
        HostNetworkInterface bridge = new("docker0", Gateways: [], Addresses: [IPAddress.Parse("172.17.0.1")]);
        FakeFoundryHostSystem host = new(WslOsRelease, [bridge, WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem(DockerDesktop), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:172.20.5.9");
    }

    [Fact]
    public async Task WhenTheOsReleaseNamesMicrosoftInCapitals_TreatsFoundryAsRunningInWsl()
    {
        // Arrange
        FakeFoundryHostSystem host = new("4.4.0-19041-Microsoft", [WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem(DockerDesktop), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:172.20.5.9");
    }

    [Fact]
    public async Task WhenFoundryDoesNotRunInWsl_KeepsTheHostGatewayWithoutAskingTheEngine()
    {
        // Arrange
        FakeSystemOperations engine = new FakeSystemOperations().WithOperatingSystem(DockerDesktop);
        FakeFoundryHostSystem host = new("6.8.0-45-generic", [WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(engine, host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:host-gateway");
        engine.SystemInfoCalls.ShouldBe(0);
    }

    [Fact]
    public async Task WhenTheOsReleaseCannotBeRead_KeepsTheHostGateway()
    {
        // Arrange
        FakeFoundryHostSystem host = new(osRelease: null, [WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem(DockerDesktop), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:host-gateway");
    }

    [Fact]
    public async Task WhenFoundryRunsInWslAgainstANativeEngine_KeepsTheHostGateway()
    {
        // Arrange
        FakeFoundryHostSystem host = new(WslOsRelease, [WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem("Ubuntu 24.04.1 LTS"), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:host-gateway");
        _logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoInterfaceCarriesAnIpv4DefaultRoute_FallsBackToTheHostGatewayWithOneWarning()
    {
        // Arrange
        HostNetworkInterface ipv6Only = new(
            "eth0",
            Gateways: [IPAddress.Parse("fe80::1")],
            Addresses: [IPAddress.Parse("fe80::215:5dff:fe12:3456")]);
        FakeFoundryHostSystem host = new(WslOsRelease, [Loopback, ipv6Only]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem(DockerDesktop), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:host-gateway");
        (LogLevel Level, string Message, Exception? _) warning = _logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("host-gateway");
    }

    [Fact]
    public async Task WhenTheDefaultRouteInterfaceHasNoIpv4Address_FallsBackToTheHostGatewayWithOneWarning()
    {
        // Arrange
        HostNetworkInterface noIpv4 = new(
            "eth0",
            Gateways: [IPAddress.Parse("172.20.0.1")],
            Addresses: [IPAddress.Parse("fe80::215:5dff:fe12:3456")]);
        FakeFoundryHostSystem host = new(WslOsRelease, [noIpv4]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem(DockerDesktop), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:host-gateway");
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task WhenTheEngineInfoCannotBeRead_FallsBackToTheHostGatewayWithOneWarning()
    {
        // Arrange
        FakeFoundryHostSystem host = new(WslOsRelease, [WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithFailedSystemInfo(), host);

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:host-gateway");
        (LogLevel Level, string Message, Exception? _) warning = _logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("host-gateway");
    }

    [Fact]
    public async Task WhenTheWslAddressChangesBetweenDispatches_TheNextDispatchMapsTheNewAddress()
    {
        // Arrange
        FakeFoundryHostSystem host = new(WslOsRelease, [WslEth0]);
        WorkerHostEntryResolver sut = BuildResolver(new FakeSystemOperations().WithOperatingSystem(DockerDesktop), host);
        await sut.ResolveAsync(TestContext.Current.CancellationToken);
        host.Interfaces =
        [
            new HostNetworkInterface("eth0", Gateways: [IPAddress.Parse("172.29.16.1")], Addresses: [IPAddress.Parse("172.29.20.4")]),
        ];

        // Act
        string entry = await sut.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        entry.ShouldBe("host.docker.internal:172.29.20.4");
    }

    private sealed class FakeFoundryHostSystem(string? osRelease, IReadOnlyList<HostNetworkInterface> interfaces)
        : IFoundryHostSystem
    {
        public IReadOnlyList<HostNetworkInterface> Interfaces { get; set; } = interfaces;

        public string? ReadKernelOsRelease() => osRelease;

        public IReadOnlyList<HostNetworkInterface> ListNetworkInterfaces() => Interfaces;
    }
}
