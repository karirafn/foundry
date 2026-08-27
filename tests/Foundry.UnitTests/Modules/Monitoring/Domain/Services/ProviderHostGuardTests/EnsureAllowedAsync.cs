using System.Net;

using Foundry.Modules.Monitoring.Domain.Services;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Settings.Contracts.Queries;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Fakes.Monitoring;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.Services.ProviderHostGuardTests;

public sealed class EnsureAllowedAsync
{
    private static BaseUrl GitHubBaseUrl() => BaseUrl.Create("https://github.com").ValueOrThrow();
    private static BaseUrl GitLabBaseUrl() => BaseUrl.Create("https://gitlab.com").ValueOrThrow();

    private static ProviderHostGuard BuildGuard(
        FakeHostAddressResolver? resolver = null,
        IGlobalSettingsQueries? settings = null)
    {
        return new ProviderHostGuard(
            settings ?? new StubGlobalSettingsQueries([]),
            resolver ?? new FakeHostAddressResolver());
    }

    [Fact]
    public async Task WhenGitHubComWithEmptyAllowlist_ReturnsOk()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("140.82.112.4")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenGitLabComWithEmptyAllowlist_ReturnsOk()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            new FakeHostAddressResolver()
                .WithAddresses("gitlab.com", IPAddress.Parse("172.65.251.78")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitLabBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenUnknownHostWithEmptyAllowlist_ReturnsNotAllowed()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            new FakeHostAddressResolver()
                .WithAddresses("evil.example.com", IPAddress.Parse("140.82.112.4")));
        BaseUrl baseUrl = BaseUrl.Create("https://evil.example.com").ValueOrThrow();

        // Act
        Result result = await sut.EnsureAllowedAsync(baseUrl, TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.NotAllowed");
        failure.Error.Message.ShouldContain("evil.example.com");
    }

    [Fact]
    public async Task WhenUnknownHostInOperatorAllowlist_ReturnsOk()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("self.hosted.example", IPAddress.Parse("140.82.112.4")),
            settings: new StubGlobalSettingsQueries(["self.hosted.example"]));
        BaseUrl baseUrl = BaseUrl.Create("https://self.hosted.example").ValueOrThrow();

        // Act
        Result result = await sut.EnsureAllowedAsync(baseUrl, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToRfc1918_10_x_ReturnsPrivateAddress()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("10.0.0.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToLoopback_ReturnsPrivateAddress()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Loopback));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToIPv6Loopback_ReturnsPrivateAddress()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.IPv6Loopback));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToLinkLocal_169_254_x_ReturnsPrivateAddress()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("169.254.1.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToRfc1918_172_16_x_ReturnsPrivateAddress()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("172.16.0.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToRfc1918_172_31_x_ReturnsPrivateAddress()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("172.31.255.255")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToRfc1918_192_168_x_ReturnsPrivateAddress()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("192.168.1.100")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToIPv6UniqueLocal_ReturnsPrivateAddress()
    {
        // Arrange — fc00::/7 unique-local (covers fc00:: through fdff::)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("fd00::1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToIPv6LinkLocal_ReturnsPrivateAddress()
    {
        // Arrange — fe80::/10 link-local
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("fe80::1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToIPv4MappedPrivate_ReturnsPrivateAddress()
    {
        // Arrange — ::ffff:10.0.0.1 (IPv4-mapped IPv6 for 10.0.0.1)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("::ffff:10.0.0.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToPublicAddress_ReturnsOk()
    {
        // Arrange
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("140.82.112.4")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenHostMatchIsCaseInsensitive_ReturnsOk()
    {
        // Arrange — Uri normalises "GitHub.Com" to lowercase "github.com"; the guard must
        // pass that normalised key to the resolver and accept a public address returned for it.
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("140.82.112.4")));

        BaseUrl baseUrl = BaseUrl.Create("https://GitHub.Com").ValueOrThrow();

        // Act
        Result result = await sut.EnsureAllowedAsync(baseUrl, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenOperatorAllowlistMatchIsCaseInsensitive_ReturnsOk()
    {
        // Arrange — operator stores "SELF.HOSTED.EXAMPLE", URL uses lowercase
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("self.hosted.example", IPAddress.Parse("140.82.112.4")),
            settings: new StubGlobalSettingsQueries(["SELF.HOSTED.EXAMPLE"]));
        BaseUrl baseUrl = BaseUrl.Create("https://self.hosted.example").ValueOrThrow();

        // Act
        Result result = await sut.EnsureAllowedAsync(baseUrl, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToCgnat_100_64_x_ReturnsPrivateAddress()
    {
        // Arrange — 100.64.0.0/10 CGNAT (RFC 6598)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("100.100.0.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToTestNet1_192_0_2_x_ReturnsPrivateAddress()
    {
        // Arrange — 192.0.2.0/24 TEST-NET-1 (RFC 5737)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("192.0.2.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToTestNet2_198_51_100_x_ReturnsPrivateAddress()
    {
        // Arrange — 198.51.100.0/24 TEST-NET-2 (RFC 5737)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("198.51.100.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToTestNet3_203_0_113_x_ReturnsPrivateAddress()
    {
        // Arrange — 203.0.113.0/24 TEST-NET-3 (RFC 5737)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("203.0.113.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToReservedClassE_240_x_ReturnsPrivateAddress()
    {
        // Arrange — 240.0.0.0/4 reserved / former Class E (RFC 1112)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("240.0.0.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToThisNetwork_0_x_ReturnsPrivateAddress()
    {
        // Arrange — 0.0.0.0/8 "this host on this network" (RFC 1122)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("0.1.2.3")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToCgnatLowerBoundary_100_63_255_255_ReturnsOk()
    {
        // Arrange — 100.63.255.255 is just BELOW CGNAT (100.64.0.0/10); must be allowed
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("100.63.255.255")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToCgnatUpperBoundary_100_128_0_0_ReturnsOk()
    {
        // Arrange — 100.128.0.0 is just ABOVE CGNAT (100.64.0.0/10 ends at 100.127.255.255); must be allowed
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("100.128.0.0")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToNat64_64_ff9b_x_ReturnsPrivateAddress()
    {
        // Arrange — 64:ff9b::0a00:0001 is the NAT64 translation of 10.0.0.1 (RFC 6146/7050)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("64:ff9b::0a00:0001")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToSixToFour_2002_x_ReturnsPrivateAddress()
    {
        // Arrange — 2002::1 is in the 6to4 prefix (2002::/16, RFC 3056)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("2002::1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToRfc2544Benchmarking_198_18_x_ReturnsPrivateAddress()
    {
        // Arrange — 198.18.0.1 is in 198.18.0.0/15 (RFC 2544 benchmarking)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("198.18.0.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

    [Fact]
    public async Task WhenAllowedHostResolvesToRfc6890IetfProtocol_192_0_0_x_ReturnsPrivateAddress()
    {
        // Arrange — 192.0.0.1 is in 192.0.0.0/24 (RFC 6890 IETF protocol assignments)
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("github.com", IPAddress.Parse("192.0.0.1")));

        // Act
        Result result = await sut.EnsureAllowedAsync(GitHubBaseUrl(), TestContext.Current.CancellationToken);

        // Assert
        Result.Failure failure = result.ShouldBeOfType<Result.Failure>();
        failure.Error.Code.ShouldBe("ProviderHost.ResolvesToPrivateAddress");
    }

}
