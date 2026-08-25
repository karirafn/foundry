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
                .WithAddresses("evil.example.com", IPAddress.Parse("203.0.113.1")));
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
                .WithAddresses("self.hosted.example", IPAddress.Parse("203.0.113.50")),
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
        // Arrange — "GitHub.Com" should match the hard-coded "github.com"
        ProviderHostGuard sut = BuildGuard(
            resolver: new FakeHostAddressResolver()
                .WithAddresses("GitHub.Com", IPAddress.Parse("140.82.112.4")));

        // The URI normalises the host to lowercase; test with the URL to match real usage
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
                .WithAddresses("self.hosted.example", IPAddress.Parse("203.0.113.50")),
            settings: new StubGlobalSettingsQueries(["SELF.HOSTED.EXAMPLE"]));
        BaseUrl baseUrl = BaseUrl.Create("https://self.hosted.example").ValueOrThrow();

        // Act
        Result result = await sut.EnsureAllowedAsync(baseUrl, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
    }

}
