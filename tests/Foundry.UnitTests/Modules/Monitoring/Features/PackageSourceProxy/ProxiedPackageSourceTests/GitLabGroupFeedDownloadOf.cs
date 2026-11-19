using Foundry.Modules.Monitoring.Features.PackageSourceProxy;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSourceProxy.ProxiedPackageSourceTests;

public sealed class GitLabGroupFeedDownloadOf
{
    private static readonly GitLabGroupFeed Feed =
        GitLabGroupFeed.Recognize(new Uri("https://git.example/gitlab/api/v4/groups/42/-/packages/nuget/index.json"))!;

    [Fact]
    public void AProjectDownloadPath_NamesThePackageAsInThePath()
    {
        // Act
        string? packageId = Feed.PackageIdOfDownload(
            new Uri("https://git.example/gitlab/api/v4/projects/123/packages/nuget/download/Contoso.Common/1.2.0/contoso.common.1.2.0.nupkg?x=1"));

        // Assert
        packageId.ShouldBe("Contoso.Common");
    }

    [Theory]
    [InlineData("http://git.example/gitlab/api/v4/projects/123/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg")]
    [InlineData("https://other.example/gitlab/api/v4/projects/123/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg")]
    [InlineData("https://git.example:8443/gitlab/api/v4/projects/123/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg")]
    public void APathOnAnotherOrigin_IsNotADownload(string url)
    {
        // Act
        string? packageId = Feed.PackageIdOfDownload(new Uri(url));

        // Assert
        packageId.ShouldBeNull();
    }

    [Theory]
    [InlineData("/gitlab/api/v4/projects/abc/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg")]
    [InlineData("/gitlab/api/v4/projects/group%2Fproject/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg")]
    [InlineData("/gitlab/api/v4/projects/123/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg/extra")]
    [InlineData("/gitlab/api/v4/projects/123/packages/nuget/download/foo/1.0.0")]
    [InlineData("/gitlab/api/v4/projects/123/packages/nuget/download/foo/1.0.0/")]
    [InlineData("/gitlab/api/v4/projects/123/packages/nuget/metadata/foo/index.json")]
    [InlineData("/api/v4/projects/123/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg")]
    [InlineData("/other/gitlab/api/v4/projects/123/packages/nuget/download/foo/1.0.0/foo.1.0.0.nupkg")]
    [InlineData("/gitlab/api/v4/projects/123/packages/nuget/download/../../../users")]
    [InlineData("/gitlab/api/v4/projects/123/packages/nuget/download/%2E%2E/1.0.0/foo.1.0.0.nupkg")]
    [InlineData("/gitlab/api/v4/projects/123/packages/nuget/download/a%2Fb/1.0.0/foo.1.0.0.nupkg")]
    public void AnyOtherPath_IsNotADownload(string path)
    {
        // Act
        string? packageId = Feed.PackageIdOfDownload(new Uri("https://git.example" + path));

        // Assert
        packageId.ShouldBeNull();
    }
}
