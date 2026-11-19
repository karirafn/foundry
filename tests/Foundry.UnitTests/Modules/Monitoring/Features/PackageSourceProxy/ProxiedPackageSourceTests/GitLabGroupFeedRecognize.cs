using Foundry.Modules.Monitoring.Features.PackageSourceProxy;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PackageSourceProxy.ProxiedPackageSourceTests;

public sealed class GitLabGroupFeedRecognize
{
    [Theory]
    [InlineData("https://gitlab.com/api/v4/groups/42/-/packages/nuget/index.json", "/api/v4")]
    [InlineData("https://gitlab.com/api/v4/groups/my-group%2Fsub/-/packages/nuget/index.json", "/api/v4")]
    [InlineData("https://git.example:8443/gitlab/api/v4/groups/7/-/packages/nuget/index.json", "/gitlab/api/v4")]
    public void AGroupFeedServiceIndex_IsAGroupFeedWithTheApiPrefixBeforeGroups(string serviceIndexUrl, string apiPrefix)
    {
        // Act
        GitLabGroupFeed? feed = GitLabGroupFeed.Recognize(new Uri(serviceIndexUrl));

        // Assert
        feed.ShouldNotBeNull().ApiPrefix.ShouldBe(apiPrefix);
    }

    [Theory]
    [InlineData("https://gitlab.com/api/v4/projects/1/packages/nuget/index.json")]
    [InlineData("https://feed.example/v3/index.json")]
    [InlineData("https://gitlab.com/api/v4/groups/42/-/packages/nuget/query")]
    [InlineData("https://gitlab.com/api/v4/groups/42/packages/nuget/index.json")]
    [InlineData("https://gitlab.com/groups/42/-/packages/nuget/index.json")]
    public void AnyOtherServiceIndex_IsNotAGroupFeed(string serviceIndexUrl)
    {
        // Act
        GitLabGroupFeed? feed = GitLabGroupFeed.Recognize(new Uri(serviceIndexUrl));

        // Assert
        feed.ShouldBeNull();
    }
}
