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

    private static readonly ProxiedPackageSource GroupFeed = new(
        PackageSourceId.New(),
        new Uri("https://gitlab.com/api/v4/groups/42/-/packages/nuget/index.json"),
        new Uri("http://host.docker.internal:5287/package-sources/"));

    [Fact]
    public void APathUnderTheServiceIndexDirectory_IsTheSamePathAndQueryOnTheSourceOrigin()
    {
        // Act
        ProxiedRequest? request = Source.UpstreamUrlFor("/v3/registration/foo/index.json", "?semVerLevel=2.0.0");

        // Assert
        request.ShouldBeOfType<ProxiedRequest.UnderDirectory>().Url
            .ShouldBe(new Uri("https://feed.example/v3/registration/foo/index.json?semVerLevel=2.0.0"));
    }

    [Theory]
    [InlineData("/v4/index.json")]
    [InlineData("/v3/../admin")]
    [InlineData("/v3x/index.json")]
    [InlineData("/")]
    public void APathOutsideTheDirectory_IsRefused(string path)
    {
        // Act
        ProxiedRequest? request = Source.UpstreamUrlFor(path, string.Empty);

        // Assert
        request.ShouldBeNull();
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
        ProxiedRequest? request = source.UpstreamUrlFor("/nuget/", string.Empty);

        // Assert
        request.ShouldBeOfType<ProxiedRequest.UnderDirectory>().Url.ShouldBe(new Uri("https://feed.example/nuget/"));
    }

    [Fact]
    public void AProjectDownloadPathOfAGroupFeed_IsADownloadToConfirm()
    {
        // Act
        ProxiedRequest? request = GroupFeed.UpstreamUrlFor(
            "/api/v4/projects/9/packages/nuget/download/Foo/1.0.0/foo.1.0.0.nupkg",
            "?v=1");

        // Assert
        ProxiedRequest.GroupFeedDownload download = request.ShouldBeOfType<ProxiedRequest.GroupFeedDownload>();
        download.ShouldSatisfyAllConditions(
            () => download.Url.ShouldBe(new Uri("https://gitlab.com/api/v4/projects/9/packages/nuget/download/Foo/1.0.0/foo.1.0.0.nupkg?v=1")),
            () => download.PackageId.ShouldBe("Foo"));
    }

    [Fact]
    public void AProjectDownloadPathReachedThroughDotSegments_IsRefused()
    {
        // Act
        ProxiedRequest? request = GroupFeed.UpstreamUrlFor(
            "/api/v4/projects/9/packages/nuget/download/../../../users",
            string.Empty);

        // Assert
        request.ShouldBeNull();
    }

    [Fact]
    public void AProjectDownloadPathOfAnyOtherSource_IsRefused()
    {
        // Arrange
        ProxiedPackageSource projectFeed = new(
            PackageSourceId.New(),
            new Uri("https://gitlab.com/api/v4/projects/9/packages/nuget/index.json"),
            new Uri("http://host.docker.internal:5287/package-sources/"));

        // Act
        ProxiedRequest? request = projectFeed.UpstreamUrlFor(
            "/api/v4/projects/8/packages/nuget/download/Foo/1.0.0/foo.1.0.0.nupkg",
            string.Empty);

        // Assert
        request.ShouldBeNull();
    }
}
