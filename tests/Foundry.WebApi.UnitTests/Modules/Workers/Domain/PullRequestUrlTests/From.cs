using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.PullRequestUrlTests;

public sealed class From
{
    [Fact]
    public void WhenCalledWithValidAbsoluteUri_ReturnsPullRequestUrlWithSameValue()
    {
        // Arrange
        const string rawUrl = "https://github.com/owner/repo/pull/10";

        // Act
        PullRequestUrl pullRequestUrl = PullRequestUrl.From(rawUrl);

        // Assert
        pullRequestUrl.Value.ShouldBe(rawUrl);
    }

    [Fact]
    public void WhenCalledWithSameString_ProducesEqualUrls()
    {
        // Arrange
        const string rawUrl = "https://github.com/owner/repo/pull/42";

        // Act
        PullRequestUrl a = PullRequestUrl.From(rawUrl);
        PullRequestUrl b = PullRequestUrl.From(rawUrl);

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenCalledWithDifferentStrings_ProducesDistinctUrls()
    {
        // Arrange

        // Act
        PullRequestUrl a = PullRequestUrl.From("https://github.com/owner/repo/pull/1");
        PullRequestUrl b = PullRequestUrl.From("https://github.com/owner/repo/pull/2");

        // Assert
        a.ShouldNotBe(b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("relative/path")]
    public void WhenCalledWithInvalidUri_ThrowsArgumentException(string value)
    {
        // Arrange

        // Act / Assert
        Should.Throw<ArgumentException>(() => PullRequestUrl.From(value));
    }
}
