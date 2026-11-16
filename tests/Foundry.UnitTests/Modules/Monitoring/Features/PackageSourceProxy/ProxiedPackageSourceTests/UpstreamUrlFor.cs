using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.PackageSourceProxy;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSourceProxy.ProxiedPackageSourceTests;

public sealed class UpstreamUrlFor
{
    private static readonly ProxiedPackageSource Source = new(
        PackageSourceId.New(),
        new Uri("https://feed.example/v3/index.json"),
        new Uri("http://host.docker.internal:5287/package-sources/"));

    [Fact]
    public void APathUnderTheServiceIndexDirectory_IsTheSamePathAndQueryOnTheSourceOrigin()
    {
        // Act
        Uri? url = Source.UpstreamUrlFor("/v3/registration/foo/index.json", "?semVerLevel=2.0.0");

        // Assert
        url.ShouldBe(new Uri("https://feed.example/v3/registration/foo/index.json?semVerLevel=2.0.0"));
    }

    [Theory]
    [InlineData("/v4/index.json")]
    [InlineData("/v3/../admin")]
    [InlineData("/v3x/index.json")]
    [InlineData("/")]
    public void APathOutsideTheDirectory_IsRefused(string path)
    {
        // Act
        Uri? url = Source.UpstreamUrlFor(path, string.Empty);

        // Assert
        url.ShouldBeNull();
    }

    [Fact]
    public void AServiceIndexUrlEndingInASlash_IsItsOwnDirectory()
    {
        // Arrange
        ProxiedPackageSource source = new(
            PackageSourceId.New(),
            new Uri("https://feed.example/nuget/"),
            new Uri("http://host.docker.internal:5287/package-sources/"));

        // Act
        Uri? url = source.UpstreamUrlFor("/nuget/", string.Empty);

        // Assert
        url.ShouldBe(new Uri("https://feed.example/nuget/"));
    }
}
